using ConditioningControlPanel.Views.Controls.Companion;
using Xunit;

namespace CCP.Core.Tests;

public sealed class CompanionLinkNavigationTests
{
    [Theory]
    [InlineData("https://127.0.0.1/video", true, true)]
    [InlineData("http://127.0.0.1/video", true, false)]
    [InlineData("file:///tmp/video.mp4", false, false)]
    [InlineData("javascript:alert(1)", false, false)]
    [InlineData("/relative", false, false)]
    [InlineData("", false, false)]
    [InlineData(null, false, false)]
    public void NavigationKeepsTheWpfSchemeAndFallbackRules(string? url, bool allowed, bool external)
    {
        Assert.Equal(allowed, CompanionLinkNavigation.TryBrowserUri(url, out var uri));
        if (allowed) Assert.Equal(external, CompanionLinkNavigation.CanOpenExternally(uri!));
    }
}
