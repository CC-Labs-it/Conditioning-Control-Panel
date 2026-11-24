using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// The Windows ghost mirror, WPF ConditioningControlPanel/Services/Fyp/FypGhostOverlay.cs in its measured
/// configuration: a plain GDI window painted RGB(1,1,1), colour-keyed out of composition (LWA_COLORKEY, never
/// LWA_ALPHA), layered, click-through, no-activate and topmost, carrying a live DWM thumbnail of the parked feed
/// window at the user's opacity (DWM_TNP_OPACITY). A GDI window and not an Avalonia one: a DirectComposition
/// surface never matches the colour key (FypGhostOverlay.cs:28). Nothing here touches the feed window itself.
/// </summary>
internal sealed class Win32GhostMirror : IDisposable
{
    private readonly IntPtr _source;
    private IntPtr _hwnd, _thumb;
    private double _opacity;

    internal string Diag { get; private set; } = "(not measured)";

    private Win32GhostMirror(IntPtr source, double opacity) { _source = source; _opacity = Clamp(opacity); }

    /// <summary>Opens the mirror over <paramref name="bounds"/> (physical px) or says why not (WPF Show, :94).
    /// Leaves nothing on screen on refusal.</summary>
    internal static (IDisposable? Mirror, string? Why) Open(TopLevel source, PixelRect bounds, double opacity)
    {
        var handle = OperatingSystem.IsWindows() ? source.TryGetPlatformHandle() : null;
        if (handle?.HandleDescriptor != "HWND" || handle.Handle == IntPtr.Zero) return (null, "unsupported");
        var mirror = new Win32GhostMirror(handle.Handle, opacity);
        string? why;
        try { why = mirror.Show(bounds); }
        catch (Exception ex) { mirror.Diag = "threw: " + ex.Message; why = "mirror-threw"; }
        Log.Information("[Game] fyp: ghost mirror: {Diag}", mirror.Diag);   // every enter, as WPF :568
        if (why == null) return (mirror, null);
        mirror.Dispose();
        return (null, why);
    }

    /// <summary>WPF FypGhostOverlay.Diagnose (:142): null when the mirror is good to show.</summary>
    internal static string? Diagnose(bool compositionEnabled, bool colorKeyApplied, int registerHr, int sourceWidth, int sourceHeight)
    {
        if (!compositionEnabled) return "composition-disabled";
        if (!colorKeyApplied) return "colorkey-rejected";
        if (registerHr != 0) return $"thumbnail-register-failed-0x{registerHr:X8}";
        if (sourceWidth <= 0 || sourceHeight <= 0) return "thumbnail-source-empty";
        return null;
    }

    private string? Show(PixelRect b)
    {
        int compHr = DwmIsCompositionEnabled(out bool composed);
        _hwnd = CreateWindowEx(WsExLayered | WsExTransparent | WsExNoActivate | WsExToolWindow | WsExTopmost,
            ClassName(), "For You", WsPopup, b.X, b.Y, b.Width, b.Height, IntPtr.Zero, IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);
        if (_hwnd == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        ShowWindow(_hwnd, 4 /* SW_SHOWNOACTIVATE */);
        UpdateWindow(_hwnd);
        // Read back (WPF ApplyColorKey :223): a key that did not take is an opaque black sheet.
        bool keyOk = SetLayeredWindowAttributes(_hwnd, ColorKey, 0, LwaColorKey)
            && GetLayeredWindowAttributes(_hwnd, out uint key, out _, out uint flags)
            && (flags & LwaColorKey) != 0 && key == ColorKey;
        int regHr = DwmRegisterThumbnail(_hwnd, _source, out _thumb);
        if (regHr == 0 && _thumb == IntPtr.Zero) regHr = unchecked((int)0x80004005);
        Size size = default;
        int srcHr = regHr == 0 ? DwmQueryThumbnailSourceSize(_thumb, out size) : regHr;
        if (srcHr != 0) size = default;
        Update();
        Diag = $"composition={composed} (0x{compHr:X8}), colorkey={keyOk}, register=0x{regHr:X8}, "
             + $"sourceSize={size.Cx}x{size.Cy} (0x{srcHr:X8}), dest={b.Width}x{b.Height}, opacity={_opacity:0.00}";
        return Diagnose(compHr == 0 && composed, keyOk, regHr, size.Cx, size.Cy);
    }

    /// <summary>WPF SetOpacity (:182): the alpha rides the thumbnail, never the window.</summary>
    internal void SetOpacity(double value) { _opacity = Clamp(value); Update(); }

    // WPF UpdateThumbnailBounds (:315): cover the whole destination, cropping the source symmetrically.
    private void Update()
    {
        if (_thumb == IntPtr.Zero || !GetClientRect(_hwnd, out var dest)) return;
        int dw = Math.Max(1, dest.Right), dh = Math.Max(1, dest.Bottom);
        var p = new ThumbProps
        {
            Flags = TnpRectDestination | TnpVisible | TnpSourceClientAreaOnly | TnpOpacity,
            Destination = new Rect32 { Right = dw, Bottom = dh },
            Opacity = (byte)Math.Round(_opacity * 255.0), Visible = true, SourceClientAreaOnly = true,
        };
        if (GetClientRect(_source, out var src) && src.Right > 0 && src.Bottom > 0)
        {
            double da = (double)dw / dh, sa = (double)src.Right / src.Bottom;
            var r = new Rect32 { Right = src.Right, Bottom = src.Bottom };
            if (sa > da) { int w = Math.Max(1, (int)Math.Round(src.Bottom * da)); r.Left = (src.Right - w) / 2; r.Right = r.Left + w; }
            else if (sa < da) { int h = Math.Max(1, (int)Math.Round(src.Right / da)); r.Top = (src.Bottom - h) / 2; r.Bottom = r.Top + h; }
            p.Flags |= TnpRectSource;
            p.Source = r;
        }
        DwmUpdateThumbnailProperties(_thumb, ref p);
    }

    /// <summary>WPF Close (:200): thumbnail dropped, window destroyed. Idempotent.</summary>
    public void Dispose()
    {
        var (thumb, hwnd) = (_thumb, _hwnd);
        (_thumb, _hwnd) = (IntPtr.Zero, IntPtr.Zero);
        try { if (thumb != IntPtr.Zero) DwmUnregisterThumbnail(thumb); }
        catch (Exception ex) { Log.Debug("[Game] fyp: ghost mirror unregister: {E}", ex.Message); }
        try { if (hwnd != IntPtr.Zero) DestroyWindow(hwnd); }
        catch (Exception ex) { Log.Debug("[Game] fyp: ghost mirror destroy: {E}", ex.Message); }
    }

    private static double Clamp(double v) => double.IsFinite(v) ? Math.Clamp(v, 0.01, 1.0) : 1.0;

    // One window class, DefWindowProcW as its procedure (no managed callback to keep alive), the key colour as its brush.
    private static string? _class;
    private static string ClassName()
    {
        if (_class != null) return _class;
        var wc = new WndClassEx
        {
            Size = (uint)Marshal.SizeOf<WndClassEx>(), Instance = GetModuleHandle(null), ClassName = "CCPFypGhostMirror",
            WndProc = GetProcAddress(GetModuleHandle("user32.dll"), "DefWindowProcW"), Background = CreateSolidBrush(ColorKey),
        };
        if (RegisterClassEx(ref wc) == 0 && Marshal.GetLastWin32Error() != 1410 /* ERROR_CLASS_ALREADY_EXISTS */)
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return _class = wc.ClassName;
    }

    private const uint WsPopup = 0x80000000, WsExTopmost = 0x8, WsExTransparent = 0x20, WsExToolWindow = 0x80,
        WsExLayered = 0x80000, WsExNoActivate = 0x08000000, LwaColorKey = 1, ColorKey = 0x00010101;
    private const int TnpRectDestination = 1, TnpRectSource = 2, TnpOpacity = 4, TnpVisible = 8, TnpSourceClientAreaOnly = 0x10;

    [StructLayout(LayoutKind.Sequential)] private struct Rect32 { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct Size { public int Cx, Cy; }
    [StructLayout(LayoutKind.Sequential)]
    private struct ThumbProps
    {
        public int Flags;
        public Rect32 Destination, Source;
        public byte Opacity;
        [MarshalAs(UnmanagedType.Bool)] public bool Visible;
        [MarshalAs(UnmanagedType.Bool)] public bool SourceClientAreaOnly;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClassEx
    {
        public uint Size, Style;
        public IntPtr WndProc;
        public int ClsExtra, WndExtra;
        public IntPtr Instance, Icon, Cursor, Background;
        public string? MenuName;
        public string ClassName;
        public IntPtr IconSm;
    }

    [DllImport("dwmapi.dll")] private static extern int DwmRegisterThumbnail(IntPtr dest, IntPtr src, out IntPtr thumb);
    [DllImport("dwmapi.dll")] private static extern int DwmUnregisterThumbnail(IntPtr thumb);
    [DllImport("dwmapi.dll")] private static extern int DwmUpdateThumbnailProperties(IntPtr thumb, ref ThumbProps props);
    [DllImport("dwmapi.dll")] private static extern int DwmQueryThumbnailSourceSize(IntPtr thumb, out Size size);
    [DllImport("dwmapi.dll")] private static extern int DwmIsCompositionEnabled([MarshalAs(UnmanagedType.Bool)] out bool enabled);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint key, byte alpha, uint flags);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetLayeredWindowAttributes(IntPtr hwnd, out uint key, out byte alpha, out uint flags);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hwnd, out Rect32 rect);
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CreateWindowExW")]
    private static extern IntPtr CreateWindowEx(uint exStyle, string cls, string title, uint style, int x, int y, int w, int h,
        IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "RegisterClassExW")]
    private static extern ushort RegisterClassEx(ref WndClassEx wc);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int cmd);
    [DllImport("user32.dll")] private static extern bool UpdateWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateSolidBrush(uint color);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetModuleHandleW")] private static extern IntPtr GetModuleHandle(string? name);
    [DllImport("kernel32.dll", CharSet = CharSet.Ansi)] private static extern IntPtr GetProcAddress(IntPtr module, string name);
}
