using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Localization;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class DashboardShellGapTests
{
    private static void EnsureApp()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    private static byte[] Pixels(Bitmap bitmap)
    {
        var pixels = new byte[bitmap.PixelSize.Width * bitmap.PixelSize.Height * 4];
        var memory = Marshal.AllocHGlobal(pixels.Length);
        try
        {
            bitmap.CopyPixels(new PixelRect(bitmap.PixelSize), memory, pixels.Length, bitmap.PixelSize.Width * 4);
            Marshal.Copy(memory, pixels, 0, pixels.Length);
            return pixels;
        }
        finally { Marshal.FreeHGlobal(memory); }
    }

    [Fact]
    public void LogoAndWeeklyPassShareTheInsetAndResizeTogether() => AvaloniaTestDispatcher.Run(() =>
    {
        EnsureApp();
        var page = new SettingsTabView();
        var logo = page.FindControl<Border>("LogoFaceLogo")!;
        var pass = page.FindControl<Border>("IntakePassFace")!;
        var host = page.FindControl<Grid>("LogoFlipHost")!;
        Assert.Equal(new Thickness(24), logo.Margin);
        Assert.Equal(logo.Margin, pass.Margin);
        Assert.True(logo.ClipToBounds);
        Assert.Equal(new CornerRadius(12), logo.CornerRadius);
        pass.IsVisible = true;
        foreach (double width in new[] { 300.0, 460.0 })
        {
            host.Measure(new Size(width, 220));
            host.Arrange(new Rect(0, 0, width, 220));
            Assert.Equal(width - 48, logo.Bounds.Width, 2);
            Assert.Equal(logo.Bounds.Size, pass.Bounds.Size);
        }

        // The rounded clip is real: paint the logo's content solid and its corner stays empty.
        pass.IsVisible = false;
        var content = Assert.IsAssignableFrom<Panel>(logo.Child);
        content.Background = Brushes.White;
        logo.Height = 100;   // content stretches to the logo, so its corner IS the logo's corner
        host.Measure(new Size(300, 220));
        host.Arrange(new Rect(0, 0, 300, 220));
        using var bitmap = new RenderTargetBitmap(new PixelSize(300, 220));
        bitmap.Render(host);
        var pixels = Pixels(bitmap);
        byte Alpha(double x, double y) => pixels[4 * ((int)y * 300 + (int)x) + 3];
        var at = logo.TranslatePoint(default, host)!.Value;   // Bounds is parent-relative, not host-relative
        Assert.True(Alpha(at.X + logo.Bounds.Width / 2, at.Y + 50) > 200, "logo content did not paint");
        Assert.True(Alpha(at.X + 1, at.Y + 1) < 40, "logo corner is square: ClipToBounds ignored CornerRadius");
    });

    [Fact]
    public void DiscordUsesARoundBlueBubbleWithItsLabelOnDemand() => AvaloniaTestDispatcher.Run(() =>
    {
        EnsureApp();
        var settings = global::ConditioningControlPanel.CoreSettings.Current;
        var motionBefore = settings.MotionLevel;
        settings.MotionLevel = global::ConditioningControlPanel.Models.MotionLevel.Off;
        Window? window = null;
        try
        {
            var page = new SettingsTabView();
            window = new Window { Content = page, Width = 1280, Height = 900 };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var bar = page.FindControl<HoverBubbleBar>("HomeBubbleBar")!;
            var discord = page.FindControl<Button>("BtnDiscord")!;
            Assert.Contains(discord, bar.Bubbles);
            Assert.Equal(Color.Parse("#5865F2"), bar.RestFillOf(discord));
            Assert.Equal(HoverBubbleBar.StripLeadingGlyph(Loc.Get("label_join_discord")), bar.LabelOf(discord));
            Assert.Equal(Loc.Get("tooltip_join_the_discord_community"), ToolTip.GetTip(discord));
            var plate = Assert.IsType<Border>(discord.Content);
            var row = Assert.IsType<StackPanel>(plate.Child);
            var slot = Assert.IsType<Border>(row.Children[1]);
            var mark = Assert.IsType<global::Avalonia.Controls.Shapes.Path>(slot.Child);
            Assert.NotNull(mark.Data);
            Assert.Equal(15, mark.Height);
            bar.Expand(discord, animate: false);
            Assert.True(bar.IsExpanded(discord));
            Assert.True(bar.LabelWidthOf(discord) > 0);
            var next = bar.Bubbles.First(b => b != discord);
            bar.Expand(next, animate: false);
            Assert.False(bar.IsExpanded(discord));
            Assert.Equal(0, bar.LabelWidthOf(discord));
            Dispatcher.UIThread.RunJobs();
        }
        finally { window?.Close(); settings.MotionLevel = motionBefore; }
    });
}
