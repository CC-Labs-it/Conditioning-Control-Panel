using ConditioningControlPanel.Avalonia.Platform;
using Xunit;
namespace CCP.Avalonia.Tests;
public sealed class TrailerBrowserPolicyTests {
    private sealed class Settings : TrailerBrowserPolicy.ISettings {
        public bool AreDevToolsEnabled { get; set; } = true;
        public bool AreDefaultContextMenusEnabled { get; set; } = true;
        public bool IsStatusBarEnabled { get; set; } = true;
        public bool AreBrowserAcceleratorKeysEnabled { get; set; } = true;
        public bool IsZoomControlEnabled { get; set; } = true;
        public bool IsBuiltInErrorPageEnabled { get; set; } = true;
        public bool IsWebMessageEnabled { get; set; } = true;
    }
    // WPF Controls/ChasterTrailerView.cs:150-157. Start from permissive engine defaults.
    [Fact]
    public void TrailerDisablesBrowserChromeInputErrorPageAndMessages() {
        var settings = new Settings();
        TrailerBrowserPolicy.ApplySettings(settings);
        Assert.False(settings.AreDevToolsEnabled);
        Assert.False(settings.AreDefaultContextMenusEnabled);
        Assert.False(settings.IsStatusBarEnabled);
        Assert.False(settings.AreBrowserAcceleratorKeysEnabled);
        Assert.False(settings.IsZoomControlEnabled);
        Assert.False(settings.IsBuiltInErrorPageEnabled);
        Assert.False(settings.IsWebMessageEnabled);
    }
}
