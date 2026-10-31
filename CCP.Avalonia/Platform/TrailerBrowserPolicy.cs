using System;
using Avalonia.Platform;
namespace ConditioningControlPanel.Avalonia.Platform; // 7cf2cee4dcfa; WPF Controls/ChasterTrailerView.cs:164,197-209.
internal sealed class TrailerBrowserPolicy(Action detach) : IDisposable {
    private Action? _detach = detach;
    public void Dispose() { var stop = _detach; _detach = null; stop?.Invoke(); }
    internal static IDisposable Attach(IPlatformHandle? handle, Action failed) {
        if (OperatingSystem.IsWindows() && handle is IWindowsWebView2PlatformHandle win) return Windows(win, failed);
        var view = handle switch { IGtkWebViewPlatformHandle gtk => gtk.WebKitWebView, ILinuxWpePlatformHandle wpe => wpe.WebKitWebView, _ => IntPtr.Zero };
        if (!OperatingSystem.IsLinux() || view == IntPtr.Zero) throw new NotSupportedException("Trailer browser handle unavailable");
        return new TrailerLinuxPolicy(view, failed);
    }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static IDisposable Windows(IWindowsWebView2PlatformHandle handle, Action failed) {
        var core = Microsoft.Web.WebView2.Core.CoreWebView2.CreateFromComICoreWebView2(handle.CoreWebView2);
        EventHandler<Microsoft.Web.WebView2.Core.CoreWebView2ProcessFailedEventArgs> callback = (_, _) => failed();
        ApplySettings(new WindowsSettings(core.Settings));
        core.ProcessFailed += callback;
        return new TrailerBrowserPolicy(() => core.ProcessFailed -= callback);
    }
    // WPF Controls/ChasterTrailerView.cs:150-157. Only the decorative trailer opts in.
    internal interface ISettings {
        bool AreDevToolsEnabled { set; }
        bool AreDefaultContextMenusEnabled { set; }
        bool IsStatusBarEnabled { set; }
        bool AreBrowserAcceleratorKeysEnabled { set; }
        bool IsZoomControlEnabled { set; }
        bool IsBuiltInErrorPageEnabled { set; }
        bool IsWebMessageEnabled { set; }
    }
    internal static void ApplySettings(ISettings settings) {
        settings.AreDevToolsEnabled = false;
        settings.AreDefaultContextMenusEnabled = false;
        settings.IsStatusBarEnabled = false;
        settings.AreBrowserAcceleratorKeysEnabled = false;
        settings.IsZoomControlEnabled = false;
        settings.IsBuiltInErrorPageEnabled = false;
        settings.IsWebMessageEnabled = false;
    }
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private sealed class WindowsSettings(Microsoft.Web.WebView2.Core.CoreWebView2Settings settings) : ISettings {
        public bool AreDevToolsEnabled { set => settings.AreDevToolsEnabled = value; }
        public bool AreDefaultContextMenusEnabled { set => settings.AreDefaultContextMenusEnabled = value; }
        public bool IsStatusBarEnabled { set => settings.IsStatusBarEnabled = value; }
        public bool AreBrowserAcceleratorKeysEnabled { set => settings.AreBrowserAcceleratorKeysEnabled = value; }
        public bool IsZoomControlEnabled { set => settings.IsZoomControlEnabled = value; }
        public bool IsBuiltInErrorPageEnabled { set => settings.IsBuiltInErrorPageEnabled = value; }
        public bool IsWebMessageEnabled { set => settings.IsWebMessageEnabled = value; }
    }
}
