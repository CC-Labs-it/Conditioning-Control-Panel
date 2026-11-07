using System;
using System.IO;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Win11 run 2026-10-23: the WebView2 profile landed in CCP.Avalonia.exe.WebView2 beside the exe, which a
/// normal user cannot write under Program Files. It belongs under UserData (%LOCALAPPDATA%\ConditioningControlPanel),
/// as WPF's browser_data* profiles do, in a folder of its own.</summary>
public sealed class WebViewProfileTests
{
    [Fact]
    public void TheProfileLivesUnderUserDataInItsOwnFolder()
    {
        var folder = Path.GetFullPath(WebViewProfile.Folder);
        Assert.Equal(Path.Combine(Path.GetFullPath(CorePaths.UserData), "browser_data_avalonia"), folder);
        // This host is a CCP_USERDATA_DIR sandbox: the profile stays inside it.
        Assert.StartsWith(Path.GetFullPath(TestUserDataProfile.Root) + Path.DirectorySeparatorChar, folder);
        // Never WPF's shared profile (opened there with other browser arguments) and never beside the exe.
        Assert.NotEqual("browser_data", Path.GetFileName(folder));
        Assert.False(folder.StartsWith(Path.GetFullPath(AppContext.BaseDirectory), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void OnlyAWebView2EnvironmentRequestIsRedirected()
    {
        Assert.False(WebViewProfile.Apply(null));
        Assert.False(WebViewProfile.Apply(EventArgs.Empty));
    }

    /// <summary>WebHost is the only NativeWebView builder, and it hands every environment request to the profile.</summary>
    [Fact]
    public void WebHostAppliesTheProfileToEveryEnvironmentRequest()
    {
        var root = AppContext.BaseDirectory;
        string? dir = root;
        while (dir != null && !File.Exists(Path.Combine(dir, "CCP.Avalonia", "CCP.Avalonia.csproj"))) dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        var source = File.ReadAllText(Path.Combine(dir!, "CCP.Avalonia", "Views", "Controls", "WebHost.axaml.cs"));
        var at = source.IndexOf("_web.EnvironmentRequested +=", StringComparison.Ordinal);
        Assert.True(at > 0);
        var handler = source.Substring(at, source.IndexOf("};", at, StringComparison.Ordinal) - at);
        Assert.Contains("WebViewProfile.Apply(e)", handler);
        Assert.Contains("ApplyWindowsOptions(e)", handler);
        Assert.DoesNotContain("if (LinuxChoice", source.Substring(Math.Max(0, at - 200), 200));   // not behind the Linux switch
    }
}
