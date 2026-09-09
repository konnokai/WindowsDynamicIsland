using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace WindowsDynamicIsland.Services;

/// <summary>
/// Observes foreground and window geometry changes without polling. Start and dispose
/// on the UI thread: out-of-context WinEvent callbacks require its message loop.
/// </summary>
public sealed class FullscreenAvoidanceService : IDisposable
{
    private const uint EventSystemForeground = 0x0003;
    private const uint EventSystemMinimizeStart = 0x0016;
    private const uint EventSystemMinimizeEnd = 0x0017;
    private const uint EventObjectLocationChange = 0x800B;
    private const uint MonitorDefaultToNearest = 2;
    private readonly nint _islandHandle;
    private readonly WinEventProc _callback;
    private readonly List<nint> _hooks = [];
    private bool _disposed;

    public event EventHandler<bool>? SuppressionChanged;
    public bool IsSuppressed { get; private set; }

    public FullscreenAvoidanceService(nint islandHandle)
    {
        _islandHandle = islandHandle;
        _callback = OnWindowEvent;
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_hooks.Count > 0)
        {
            return;
        }

        try
        {
            AddHook(EventSystemForeground, EventSystemForeground);
            AddHook(EventSystemMinimizeStart, EventSystemMinimizeEnd);
            AddHook(EventObjectLocationChange, EventObjectLocationChange);
            Refresh();
        }
        catch
        {
            ReleaseHooks();
            throw;
        }
    }

    /// <summary>Rechecks after display configuration changes or moving the island.</summary>
    public void Refresh()
    {
        if (_disposed)
        {
            return;
        }

        var suppressed = IsForegroundFullscreen();
        if (suppressed != IsSuppressed)
        {
            IsSuppressed = suppressed;
            SuppressionChanged?.Invoke(this, suppressed);
        }
    }

    private void AddHook(uint firstEvent, uint lastEvent)
    {
        var hook = SetWinEventHook(firstEvent, lastEvent, nint.Zero, _callback, 0, 0, 0);
        if (hook == nint.Zero)
        {
            throw new Win32Exception("Unable to observe fullscreen window changes.");
        }

        _hooks.Add(hook);
    }

    private void OnWindowEvent(nint hook, uint eventType, nint window, int objectId,
        int childId, uint threadId, uint eventTime)
    {
        if (eventType == EventObjectLocationChange &&
            (objectId != 0 || childId != 0 ||
             (window != GetForegroundWindow() && window != _islandHandle)))
        {
            return;
        }

        Refresh();
    }

    /// <summary>
    /// Compare the foreground client surface with the island's monitor. Client bounds
    /// exclude invisible resize borders, so ordinary maximized windows do not qualify.
    /// </summary>
    private bool IsForegroundFullscreen()
    {
        var window = GetForegroundWindow();
        if (window == nint.Zero || window == _islandHandle || window == GetDesktopWindow() ||
            window == GetShellWindow() || !IsWindowVisible(window) || IsIconic(window))
        {
            return false;
        }

        // Win32 window class names have a documented maximum length of 256 characters.
        var className = new StringBuilder(256);
        GetClassName(window, className, className.Capacity);
        if (className.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
        {
            return false;
        }

        var monitor = MonitorFromWindow(_islandHandle, MonitorDefaultToNearest);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == nint.Zero || !GetMonitorInfo(monitor, ref info) ||
            !GetClientRect(window, out var client))
        {
            return false;
        }

        var topLeft = new Point { X = client.Left, Y = client.Top };
        var bottomRight = new Point { X = client.Right, Y = client.Bottom };
        return ClientToScreen(window, ref topLeft) && ClientToScreen(window, ref bottomRight) &&
            topLeft.X <= info.Monitor.Left && topLeft.Y <= info.Monitor.Top &&
            bottomRight.X >= info.Monitor.Right && bottomRight.Y >= info.Monitor.Bottom;
    }

    public void Dispose()
    {
        _disposed = true;
        ReleaseHooks();
    }

    private void ReleaseHooks()
    {
        foreach (var hook in _hooks)
        {
            UnhookWinEvent(hook);
        }

        _hooks.Clear();
    }

    private delegate void WinEventProc(nint hook, uint eventType, nint window,
        int objectId, int childId, uint threadId, uint eventTime);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern nint SetWinEventHook(uint firstEvent, uint lastEvent, nint module,
        WinEventProc callback, uint processId, uint threadId, uint flags);
    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(nint hook);
    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern nint GetDesktopWindow();
    [DllImport("user32.dll")]
    private static extern nint GetShellWindow();
    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")]
    private static extern bool IsIconic(nint window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint window, StringBuilder className, int capacity);
    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll")]
    private static extern bool GetClientRect(nint window, out Rect rect);
    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(nint window, ref Point point);
}
