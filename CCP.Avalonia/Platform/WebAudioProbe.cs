using System;
using System.Runtime.InteropServices;
using Avalonia.Platform;
namespace ConditioningControlPanel.Avalonia.Platform;
// Engine-level audible playback for frames no page script can read. WPF observes media in every frame
// (Services/Browser/BrowserService.cs:337 document-created script, isRealPlayback = unmuted).
// Null when the engine cannot answer, so callers fail closed. UI thread only.
internal static class WebAudioProbe {
    internal static bool? IsPlaying(IPlatformHandle? handle) {
        try {
            if (OperatingSystem.IsWindows() && handle is IWindowsWebView2PlatformHandle win) return Windows(win);
            var view = handle switch { IGtkWebViewPlatformHandle gtk => gtk.WebKitWebView, ILinuxWpePlatformHandle wpe => wpe.WebKitWebView, _ => IntPtr.Zero };
            if (!OperatingSystem.IsLinux() || view == IntPtr.Zero) return null;
            var playing = -1; // stays -1 if the property is missing, which must not read as silence
            g_object_get(view, "is-playing-audio", ref playing, IntPtr.Zero);
            return playing == -1 ? null : playing != 0;
        } catch (Exception) { return null; }
    }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static bool Windows(IWindowsWebView2PlatformHandle handle) =>
        Microsoft.Web.WebView2.Core.CoreWebView2.CreateFromComICoreWebView2(handle.CoreWebView2).IsDocumentPlayingAudio;
    [DllImport("libgobject-2.0.so.0")] private static extern void g_object_get(IntPtr obj, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, ref int value, IntPtr end);
}
