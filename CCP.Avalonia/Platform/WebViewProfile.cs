using System;
using System.IO;
using Avalonia.Platform;

namespace ConditioningControlPanel.Avalonia.Platform
{
    /// <summary>
    /// Where WebView2 keeps its profile on Windows. Left alone it lands in
    /// <c>&lt;exe&gt;.WebView2</c> next to the exe (seen on Win11: <c>CCP.Avalonia.exe.WebView2\EBWebView</c>),
    /// which a normal user cannot write under Program Files. WPF keeps its profiles in
    /// <c>%LOCALAPPDATA%\ConditioningControlPanel\browser_data*</c> (App.UserDataPath); this head uses one
    /// folder of its own there, so it never opens a WPF profile with different browser arguments.
    /// Every web view of this head is a <see cref="Views.Controls.WebHost"/> and they all share this
    /// folder with the same (default) options, as WebView2 requires per user-data folder.
    /// Linux (WebKitGTK / WPE) keeps the engine's own per-user location.
    /// </summary>
    internal static class WebViewProfile
    {
        internal const string FolderName = "browser_data_avalonia";

        /// <summary>The WebView2 user-data folder: under <see cref="CorePaths.UserData"/>, so a
        /// CCP_USERDATA_DIR sandbox keeps its own profile too.</summary>
        internal static string Folder => Path.Combine(CorePaths.UserData, FolderName);

        /// <summary>NativeWebView.EnvironmentRequested: points a WebView2 environment at <see cref="Folder"/>.
        /// True when it did (Windows only).</summary>
        internal static bool Apply(object? args)
        {
            if (args is not WindowsWebView2EnvironmentRequestedEventArgs windows) return false;
            try { Directory.CreateDirectory(Folder); } catch { /* WebView2 creates it itself */ }
            windows.UserDataFolder = Folder;
            return true;
        }
    }
}
