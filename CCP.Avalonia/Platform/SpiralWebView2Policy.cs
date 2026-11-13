using System;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using Avalonia.Platform;
using Microsoft.Web.WebView2.Core;

namespace ConditioningControlPanel.Avalonia.Platform;

// WPF Controls/SpiralEmbedView.cs:178-190. Only Spiral opts into this policy (WebHost.ConfigureNative).
// Process failure is the host's own watch (WebHost.BrowserProcessFailed, TrailerBrowserPolicy), not this class.
internal static class SpiralWebView2Policy
{
    internal interface ICore
    {
        bool AreDevToolsEnabled { set; }
        bool AreDefaultContextMenusEnabled { set; }
        bool IsStatusBarEnabled { set; }
        bool AreBrowserAcceleratorKeysEnabled { set; }
        bool IsZoomControlEnabled { set; }
        bool IsBuiltInErrorPageEnabled { set; }
        bool IsWebMessageEnabled { set; }
    }

    /// <summary>Windows WebView2 cores only; any other engine is left as it is.</summary>
    internal static void Attach(IPlatformHandle? handle)
    {
        if (OperatingSystem.IsWindows() && handle is IWindowsWebView2PlatformHandle win && win.CoreWebView2 != IntPtr.Zero)
            Windows(win.CoreWebView2);
    }

    // Keep SDK activation behind the Windows adapter boundary, as TrailerBrowserPolicy does.
    [MethodImpl(MethodImplOptions.NoInlining)]
    [SupportedOSPlatform("windows")]
    private static void Windows(IntPtr core) => Apply(new NativeCore(CoreWebView2.CreateFromComICoreWebView2(core)));

    internal static void Apply(ICore core)
    {
        core.AreDevToolsEnabled = false;
        core.AreDefaultContextMenusEnabled = false;
        core.IsStatusBarEnabled = false;
        core.AreBrowserAcceleratorKeysEnabled = false;
        core.IsZoomControlEnabled = false;
        core.IsBuiltInErrorPageEnabled = false;
        core.IsWebMessageEnabled = true;
    }

    private sealed class NativeCore(CoreWebView2 core) : ICore
    {
        public bool AreDevToolsEnabled { set => core.Settings.AreDevToolsEnabled = value; }
        public bool AreDefaultContextMenusEnabled { set => core.Settings.AreDefaultContextMenusEnabled = value; }
        public bool IsStatusBarEnabled { set => core.Settings.IsStatusBarEnabled = value; }
        public bool AreBrowserAcceleratorKeysEnabled { set => core.Settings.AreBrowserAcceleratorKeysEnabled = value; }
        public bool IsZoomControlEnabled { set => core.Settings.IsZoomControlEnabled = value; }
        public bool IsBuiltInErrorPageEnabled { set => core.Settings.IsBuiltInErrorPageEnabled = value; }
        public bool IsWebMessageEnabled { set => core.Settings.IsWebMessageEnabled = value; }
    }
}
