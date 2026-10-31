using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class WallpaperCreditTests
{
    [Fact]
    public Task Credit_refreshes_on_each_show_and_uses_the_locked_link() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var s = CoreSettings.Current;
        s.MediaSource = "local";
        s.FypOnlineConsented = false;
        s.RemoteMediaConsented = true;
        var tab = new BambiTakeoverTabView();
        var credit = tab.FindControl<ScrolllerCreditLine>("TxtWallpaperCredit")!;
        Assert.False(credit.IsVisible);
        tab.IsVisible = false;
        s.MediaSource = "mixed";
        tab.IsVisible = true;
        Assert.True(credit.IsVisible);
        Assert.Equal("Media from Scrolller", ((TextBlock)credit.Link.Content!).Text);
        tab.IsVisible = false;
        s.RemoteMediaConsented = false;
        tab.IsVisible = true;
        Assert.False(credit.IsVisible);
        return Task.CompletedTask;
    });
}
