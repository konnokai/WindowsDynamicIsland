using System.Runtime.InteropServices;

namespace WindowsDynamicIsland.Services;

internal static class WindowChromeService
{
    private const int DwmwaBorderColor = 34;
    private const uint DwmColorNone = 0xFFFFFFFE;
    private const int GwlStyle = -16;
    private const int GwlExStyle = -20;
    private const long WsCaption = 0x00C00000;
    private const long WsThickFrame = 0x00040000;
    private const long WsExWindowEdge = 0x00000100;
    private const long WsExClientEdge = 0x00000200;
    private const long WsExStaticEdge = 0x00020000;
    private const uint SwpFrameChanged = 0x0020;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;

    /// <summary>Removes native frame styles and DWM outlining without moving or activating the island.</summary>
    public static void HideSystemBorder(nint windowHandle)
    {
        // Presenter configuration alone can leave a native frame. Refresh the
        // non-client area after clearing both standard and extended edge styles.
        var style = GetWindowLongPtr(windowHandle, GwlStyle).ToInt64();
        SetWindowLongPtr(windowHandle, GwlStyle, (nint)(style & ~(WsCaption | WsThickFrame)));
        var extendedStyle = GetWindowLongPtr(windowHandle, GwlExStyle).ToInt64();
        SetWindowLongPtr(windowHandle, GwlExStyle,
            (nint)(extendedStyle & ~(WsExWindowEdge | WsExClientEdge | WsExStaticEdge)));
        SetWindowPos(windowHandle, nint.Zero, 0, 0, 0, 0,
            SwpFrameChanged | SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate);

        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            return;
        }

        var borderColor = DwmColorNone;
        _ = DwmSetWindowAttribute(windowHandle, DwmwaBorderColor, ref borderColor, sizeof(uint));
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint windowHandle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(nint windowHandle, int index, nint value);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint windowHandle, nint insertAfter,
        int x, int y, int width, int height, uint flags);

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(
        nint windowHandle,
        int attribute,
        ref uint attributeValue,
        int attributeSize);
}
