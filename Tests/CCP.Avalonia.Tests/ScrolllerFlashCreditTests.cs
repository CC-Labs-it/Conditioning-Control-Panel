using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services.Flash;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ScrolllerFlashCreditTests
{
    [Fact]
    public Task MarkBelongsToPictureAndLeavesWithIt() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        Bitmap Picture() => new Bitmap(AssetLoader.Open(new Uri("avares://CCP.Avalonia/Resources/features/flash.png")));
        var flash = new FlashOverlayWindow(Picture(), "ccp_temp_remote_picture.jpg");
        var local = new FlashOverlayWindow(Picture(), "local.jpg");
        try
        {
            var mark = Assert.IsType<Border>(flash.CreditMark);
            Assert.Equal("via Scrolller", Assert.IsType<TextBlock>(mark.Child).Text);
            Assert.False(mark.IsHitTestVisible);
            Assert.Equal("via Scrolller", global::Avalonia.Automation.AutomationProperties.GetName(mark));
            Assert.Equal(0.75, mark.Opacity);
            Assert.Equal(global::Avalonia.Layout.HorizontalAlignment.Right, mark.HorizontalAlignment);
            Assert.Equal(global::Avalonia.Layout.VerticalAlignment.Bottom, mark.VerticalAlignment);
            var root = Assert.IsType<Panel>(flash.Content);
            var picture = Assert.IsType<Panel>(Assert.Single(root.Children));
            Assert.Contains(mark, picture.Children);
            Assert.Null(local.CreditMark);
            flash.BeginExit(FlashExit.Begin(FlashExitStyle.Pop, ConditioningControlPanel.Models.MotionLevel.Full, 1));
            Assert.False(mark.IsVisible);
        }
        finally { flash.Close(); local.Close(); }
        return Task.CompletedTask;
    });
}
