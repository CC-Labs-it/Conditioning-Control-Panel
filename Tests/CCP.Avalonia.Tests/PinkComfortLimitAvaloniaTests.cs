using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls.AppSettings;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// Accessibility "Limit pink filter to" on the Avalonia head (WPF 91938be4d0): off changes nothing,
/// on holds the painted tint to the limit whatever the slider asks for, the Performance switch and
/// slider write the shared Core keys and repaint a steady tint, the Pink Filter page says when the
/// limit holds its slider back, and every paint point goes through the rule.
/// </summary>
public sealed class PinkComfortLimitAvaloniaTests
{
    private static void EnsureApp()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    private static global::Avalonia.Platform.Screen Screen()
    {
        var s = (global::Avalonia.Platform.Screen)Activator.CreateInstance(typeof(global::Avalonia.Platform.PlatformScreen),
            new object[] { new global::Avalonia.Platform.PlatformHandle(new IntPtr(1), "headless-test") })!;
        typeof(global::Avalonia.Platform.Screen).GetProperty("Scaling")!.SetValue(s, 1.0);
        typeof(global::Avalonia.Platform.Screen).GetProperty("Bounds")!.SetValue(s, new PixelRect(0, 0, 800, 600));
        typeof(global::Avalonia.Platform.Screen).GetProperty("WorkingArea")!.SetValue(s, new PixelRect(0, 0, 800, 600));
        typeof(global::Avalonia.Platform.Screen).GetProperty("IsPrimary")!.SetValue(s, true);
        return s;
    }

    private static byte PaintedAlpha(TintOverlayWindow w)
        => Assert.IsType<SolidColorBrush>(Assert.IsType<Border>(w.Content).Background).Color.A;

    [Theory]
    [InlineData(false, 10, 40, 102)]   // off: 40% as asked
    [InlineData(true, 10, 40, 25)]     // on at 10%: held to 10%
    [InlineData(true, 50, 40, 102)]    // on above the asked value: unchanged
    [InlineData(true, 5, 5, 12)]       // at the limit: unchanged
    public Task The_tint_never_paints_past_the_limit(bool on, int pct, int asked, byte alpha) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var provider = CoreSettings.ServiceProvider;
        var settings = new ConditioningControlPanel.Services.SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        var s = settings.Current;
        var (skip, query, hold) = (PinkFilterOverlay.SkipPlatformChecksForTests, ScreenList.QueryOverride, PinkFilterOverlay.PulseHold);
        var host = new Window();
        try
        {
            PinkFilterOverlay.Stop();
            PinkFilterOverlay.SkipPlatformChecksForTests = true;
            PinkFilterOverlay.PulseHold = true;
            ScreenList.QueryOverride = _ => new[] { Screen() };
            (s.PinkFilterEnabled, s.GentleVisuals, s.PinkFilterOpacity) = (true, false, asked);
            (s.PinkComfortLimitOn, s.PinkComfortLimitPct) = (on, pct);
            host.Show();
            PinkFilterOverlay.Refresh(host);
            var tint = Assert.Single(PinkFilterOverlay.Shown);
            Assert.Equal(alpha, PaintedAlpha(tint));

            // A Takeover pulse asks for twice the slider: still held to the limit.
            PinkFilterOverlay.SetPulse(1.0);
            byte pulsed = PaintedAlpha(tint);
            if (on) Assert.True(pulsed <= (byte)(Math.Min(pct, 50) / 100.0 * 255));
            PinkFilterOverlay.SetPulse(null);

            // A moved limit repaints the steady tint on the next refresh (the Performance slider calls it).
            s.PinkComfortLimitOn = !on;
            PinkFilterOverlay.Refresh(host);
            Assert.Equal((byte)(Math.Clamp(ConditioningControlPanel.Looks.PinkComfort.Apply(asked / 100.0, !on, pct), 0, 1) * 255), PaintedAlpha(tint));
        }
        finally
        {
            PinkFilterOverlay.Stop(); host.Close();
            (PinkFilterOverlay.SkipPlatformChecksForTests, ScreenList.QueryOverride, PinkFilterOverlay.PulseHold) = (skip, query, hold);
            CoreSettings.ServiceProvider = provider;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task The_performance_rows_write_the_shared_keys() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        var s = CoreSettings.Current;
        var saved = (s.PinkComfortLimitOn, s.PinkComfortLimitPct);
        Window? w = null;
        try
        {
            (s.PinkComfortLimitOn, s.PinkComfortLimitPct) = (false, 10);
            var section = new PerformanceSettingsSection();
            w = new Window { Content = section, Width = 700, Height = 1400 };
            w.Show();
            Dispatcher.UIThread.RunJobs();
            var box = section.FindControl<CheckBox>("ChkPinkComfortLimit")!;
            var panel = section.FindControl<Grid>("PnlPinkComfortLimit")!;
            var slider = section.FindControl<Slider>("SliderPinkComfortLimit")!;
            var label = section.FindControl<TextBlock>("TxtPinkComfortLimit")!;
            Assert.False(box.IsChecked);
            Assert.False(panel.IsVisible);   // the slider shows only while the limit is on

            box.IsChecked = true;
            Assert.True(s.PinkComfortLimitOn);
            Assert.True(panel.IsVisible);

            slider.Value = 23;
            Assert.Equal(23, s.PinkComfortLimitPct);
            Assert.Equal("23%", label.Text);

            box.IsChecked = false;
            Assert.False(s.PinkComfortLimitOn);
            Assert.False(panel.IsVisible);
        }
        finally
        {
            w?.Close();
            (s.PinkComfortLimitOn, s.PinkComfortLimitPct) = saved;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task The_pink_page_says_when_the_limit_holds_its_slider_back() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        var s = CoreSettings.Current;
        var saved = (s.PinkComfortLimitOn, s.PinkComfortLimitPct, s.PinkFilterOpacity);
        Window? w = null;
        try
        {
            (s.PinkComfortLimitOn, s.PinkComfortLimitPct, s.PinkFilterOpacity) = (false, 10, 30);
            var page = new PinkFilterFeatureControl();
            w = new Window { Content = page, Width = 900, Height = 1400 };
            w.Show();
            Dispatcher.UIThread.RunJobs();
            var note = page.FindControl<TextBlock>("TxtComfortClip")!;
            Assert.False(note.IsVisible);

            s.PinkComfortLimitOn = true;   // written elsewhere (Settings > Performance): the page follows
            Dispatcher.UIThread.RunJobs();
            Assert.True(note.IsVisible);
            Assert.Contains("10", note.Text);

            s.PinkComfortLimitPct = 40;    // the limit is above the slider: no note
            Dispatcher.UIThread.RunJobs();
            Assert.False(note.IsVisible);
        }
        finally
        {
            w?.Close();
            (s.PinkComfortLimitOn, s.PinkComfortLimitPct, s.PinkFilterOpacity) = saved;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public void Every_paint_point_goes_through_the_rule()
    {
        var overlays = Path.Combine(RepoRoot(), "CCP.Avalonia", "Views", "Overlays");
        var pink = File.ReadAllText(Path.Combine(overlays, "PinkFilterOverlay.cs"));
        // The filter paints its windows only through Paint, which applies the limit.
        Assert.Single(Regex.Matches(pink, @"\.SetTint\("));
        Assert.Contains("w.SetTint(r, g, b, PinkComfortLimit.Apply(opacity))", pink);

        var steam = File.ReadAllText(Path.Combine(overlays, "SteamOverlay.cs"));
        Assert.Contains("PinkComfortLimit.Apply(PinkFilterOverlay.PaintedOpacity)", steam);
        Assert.Contains("PinkComfortLimit.FogStrength(SteamOptions.Strength(look.Opacity))", steam);
    }

    [Fact]
    public void The_fog_is_held_to_the_limit_and_untouched_with_it_off()
    {
        for (int pct = 0; pct <= 100; pct++)
        {
            double fog = ConditioningControlPanel.SuperCore.SteamOptions.Strength(pct / 100.0);
            Assert.Equal(fog, ConditioningControlPanel.Looks.PinkComfort.FogStrength(fog, false, 10), 9);
            Assert.True(ConditioningControlPanel.Looks.PinkComfort.FogStrength(fog, true, 10) <= 0.10 + 1e-9);
        }
    }

    private static string RepoRoot([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));
}
