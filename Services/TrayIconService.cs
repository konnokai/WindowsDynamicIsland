using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;

namespace WindowsDynamicIsland.Services;

public sealed class TrayIconService : IDisposable
{
    private const uint NifMessage = 0x00000001;
    private const uint NifIcon = 0x00000002;
    private const uint NifTip = 0x00000004;
    private const uint NimAdd = 0x00000000;
    private const uint NimDelete = 0x00000002;
    private const uint WmApp = 0x8000;
    private const uint TrayMessage = WmApp + 1;
    private const uint WmLButtonUp = 0x0202;
    private const uint WmRButtonUp = 0x0205;
    private const uint WmCommand = 0x0111;
    private const int GwlWndProc = -4;
    private const uint MenuExit = 1001;
    private const uint TpmReturnCmd = 0x0100;
    private const uint TpmRightButton = 0x0002;
    private const uint MfString = 0x00000000;

    private readonly Window _window;
    private readonly nint _windowHandle;
    private readonly WndProc _wndProc;
    private nint _previousWndProc;
    private bool _isAdded;

    public TrayIconService(Window window, nint windowHandle)
    {
        _window = window;
        _windowHandle = windowHandle;
        _wndProc = WindowProc;
    }

    public void Initialize()
    {
        _previousWndProc = SetWindowLongPtr(
            _windowHandle,
            GwlWndProc,
            Marshal.GetFunctionPointerForDelegate(_wndProc));

        var data = new NotifyIconData
        {
            cbSize = Marshal.SizeOf<NotifyIconData>(),
            hWnd = _windowHandle,
            uID = 1,
            uFlags = NifMessage | NifIcon | NifTip,
            uCallbackMessage = TrayMessage,
            hIcon = LoadIcon(nint.Zero, 32512),
            szTip = "Windows Dynamic Island"
        };

        _isAdded = Shell_NotifyIcon(NimAdd, ref data);
    }

    public void Dispose()
    {
        if (_isAdded)
        {
            var data = new NotifyIconData
            {
                cbSize = Marshal.SizeOf<NotifyIconData>(),
                hWnd = _windowHandle,
                uID = 1
            };
            Shell_NotifyIcon(NimDelete, ref data);
            _isAdded = false;
        }

        if (_previousWndProc != nint.Zero)
        {
            SetWindowLongPtr(_windowHandle, GwlWndProc, _previousWndProc);
            _previousWndProc = nint.Zero;
        }
    }

    private nint WindowProc(nint hWnd, uint message, nint wParam, nint lParam)
    {
        if (message == TrayMessage)
        {
            var trayEvent = unchecked((uint)lParam.ToInt64());
            if (trayEvent == WmLButtonUp)
            {
                _window.Activate();
            }
            else if (trayEvent == WmRButtonUp)
            {
                ShowContextMenu();
            }

            return nint.Zero;
        }

        if (message == WmCommand && unchecked((uint)wParam.ToInt64()) == MenuExit)
        {
            _window.Close();
            return nint.Zero;
        }

        return CallWindowProc(_previousWndProc, hWnd, message, wParam, lParam);
    }

    private void ShowContextMenu()
    {
        if (!GetCursorPos(out var point))
        {
            return;
        }

        var menu = CreatePopupMenu();
        if (menu == nint.Zero)
        {
            return;
        }

        AppendMenu(menu, MfString, MenuExit, "Exit");
        SetForegroundWindow(_windowHandle);
        var command = TrackPopupMenuEx(menu, TpmReturnCmd | TpmRightButton, point.X, point.Y, _windowHandle, nint.Zero);
        DestroyMenu(menu);

        if (command == MenuExit)
        {
            _window.Close();
        }
    }

    private delegate nint WndProc(nint hWnd, uint message, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public int cbSize;
        public nint hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public nint hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(uint message, ref NotifyIconData data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint LoadIcon(nint instance, int iconId);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(nint hWnd, int index, nint newValue);

    [DllImport("user32.dll")]
    private static extern nint CallWindowProc(nint previousWndProc, nint hWnd, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    private static extern nint CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(nint menu, uint flags, uint command, string text);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll")]
    private static extern uint TrackPopupMenuEx(nint menu, uint flags, int x, int y, nint hWnd, nint reserved);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(nint menu);
}
