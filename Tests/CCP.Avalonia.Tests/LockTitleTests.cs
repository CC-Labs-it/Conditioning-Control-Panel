using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Controls;
using Xunit;
using AvApp = global::ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>WPF Controls/LockTitle.cs on Avalonia: the timelines as curves, the entry/jolt on a stepped
/// clock (P08), and the idle clock parked when hidden or detached (P01/P65).</summary>
public sealed class LockTitleTests
{
    [Fact]
    public void CurvesMatchTheWpfTimelines()
    {
        // wobble: BeginTime 370 ms x i, -1.2 -> 1.2 over 2.8 s, AutoReverse
        Assert.Equal(0, LockTitle.Wobble(369, 1));
        Assert.Equal(-1.2, LockTitle.Wobble(370, 1), 6);
        Assert.Equal(1.2, LockTitle.Wobble(370 + 2800, 1), 6);
        Assert.Equal(-1.2, LockTitle.Wobble(370 + 5600, 1), 6);
        // sway 1.5 -> -1.5 over 3.3 s from 530 ms x i; breath 1 -> 1.02 over 2.6 s from 420 ms x phase
        Assert.Equal(-1.5, LockTitle.Sway(1060 + 3300, 2), 6);
        Assert.Equal(1.0, LockTitle.Breath(419, 1), 6);
        Assert.Equal(1.02, LockTitle.Breath(420 + 2600, 1), 6);
        // the drop: -70 px until its turn, a back-ease overshoot past the line, 0 at 340 ms
        Assert.Equal(-70, LockTitle.Drop(-1));
        Assert.Equal(0, LockTitle.Drop(340));
        Assert.True(Enumerable.Range(1, 339).Max(ms => LockTitle.Drop(ms)) > 0);
        // the squash: hit at 30 %, rebound at 70 %, back to 1 at 260 ms
        Assert.Equal(0.82, LockTitle.Squash(78, 0.82, 1.05), 6);
        Assert.Equal(1.05, LockTitle.Squash(182, 0.82, 1.05), 6);
        Assert.Equal(1, LockTitle.Squash(260, 0.82, 1.05));
        // the entry swing: 14 deg at 1/8, then -14 x 0.55 at 3/8, ..., hanging still at 1200 ms
        Assert.Equal(14, LockTitle.Swing(150, 14, 1200, 4), 6);
        Assert.Equal(-14 * 0.55, LockTitle.Swing(450, 14, 1200, 4), 6);
        Assert.Equal(14 * 0.55 * 0.55 * 0.55 * -1, LockTitle.Swing(1050, 14, 1200, 4), 6);
        Assert.Equal(0, LockTitle.Swing(1200, 14, 1200, 4));
    }

    private static void EnsureApp()
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    [Fact]
    public Task EveryOIsAPadlock() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        Assert.Equal(new[] { ("L", false), ("o", true), ("ckt", false), ("o", true), ("ber", false) },
            LockTitle.Split("Locktober").Select(r => (r.Run, r.Padlock)));
        Assert.Empty(LockTitle.Split(""));
        Assert.True(LockTitle.NaturalWidth("Locktober", 88) > LockTitle.NaturalWidth("Locktober", 60));
        return Task.CompletedTask;
    });

    [Fact]
    public Task EntryJoltAndIdleRunOnTheClockOnlyWhileShown() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        var now = TimeSpan.FromSeconds(100);
        var title = new LockTitle { MotionOverride = true, Now = () => now, FontSize = 88, Text = "Locktober" };
        var host = new Border { Child = title };
        var window = new Window { Width = 1000, Height = 300, Content = host };
        try
        {
            window.Show();
            Assert.Equal(2, title.PadlockCount);
            var first = title.Glyphs.First();
            var lastPadlock = title.Padlocks.Last();

            // the entry: letters first, 40 ms apart, padlocks last; a second call mid-flight is ignored
            title.PlayEntry();
            Assert.True(title.IsTicking);
            title.Apply(now + TimeSpan.FromMilliseconds(35));
            Assert.Equal(0.5, first.Opacity, 6);
            Assert.Equal(0, lastPadlock.Opacity);
            now += TimeSpan.FromMilliseconds(100);
            title.PlayEntry();
            title.Apply(now);
            Assert.Equal(1, first.Opacity);
            // a resize mid-entry rebuilds the glyphs but keeps the entry window (WPF): no rattle yet
            title.FitWidth = 900;
            title.Rattle();
            title.Apply(now + TimeSpan.FromMilliseconds(40));
            Assert.Equal(-10, Angle(title.Padlocks.First()), 3);

            // after the entry the jolt tugs the padlocks: 10 deg at a sixth of 600 ms, against a -10 lean
            now += TimeSpan.FromSeconds(5);
            title.Jolt();
            title.Apply(now + TimeSpan.FromMilliseconds(100));
            Assert.Equal(0, Angle(title.Padlocks.First()), 3);
            title.Apply(now + TimeSpan.FromMilliseconds(600));
            Assert.Equal(-10, Angle(title.Padlocks.First()), 3);

            // the idle runs at 20 fps and parks when an ancestor hides, re-arms when shown
            title.Start();
            Assert.True(title.IsTicking);
            title.Apply(now + TimeSpan.FromMilliseconds(1000));
            Assert.NotEqual(-10, Angle(title.Padlocks.First()), 3);
            host.IsVisible = false;
            Assert.False(title.IsTicking);
            host.IsVisible = true;
            Assert.True(title.IsTicking);
            window.Content = null;
            Assert.False(title.IsTicking);
            window.Content = host;
            Assert.True(title.IsTicking);

            // Stop (the jolt long done): resting pose (lean only), clock off
            now += TimeSpan.FromSeconds(1);
            title.Stop();
            Assert.False(title.IsTicking);
            Assert.Equal(-10, Angle(title.Padlocks.First()), 3);

            // motion off: nothing starts
            title.MotionOverride = false;
            title.Start();
            title.PlayEntry();
            Assert.False(title.IsRunning);
            Assert.False(title.IsTicking);
        }
        finally { window.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task ALongNameShrinksToFit() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        var title = new LockTitle { FontSize = 88, Text = "The Longest Lock Anyone Ever Wore", FitWidth = 600 };
        Assert.True(title.EffectiveFontSize < 88);
        Assert.InRange(LockTitle.NaturalWidth(title.Text, title.EffectiveFontSize), 590, 610);
        return Task.CompletedTask;
    });

    /// <summary>The Chaster hero's hooks (WPF ChasterTabView :102, Fx.cs:159/160/262/676): shown starts the
    /// idle, the card's width feeds FitWidth, a booking tugs the padlocks, hidden parks the idle.</summary>
    [Fact]
    public Task TheChasterHeroDrivesTheTitle() => AvaloniaTestDispatcher.RunAsync(() => ChasterBillTests.Run(async (chaster, shell) =>
    {
        var tab = shell.Named<ChasterTabView>("ChasterTab")!;
        var title = tab.FindControl<LockTitle>("HeroTitle")!;
        title.MotionOverride = true;
        if (string.IsNullOrEmpty(title.Text)) title.Text = "Locktober";
        shell.ShowTab("chaster");
        Dispatcher.UIThread.RunJobs();
        Assert.True(title.IsRunning);

        var card = tab.FindControl<Border>("HeroCard")!;
        Assert.True(card.Bounds.Width > 52);
        Assert.Equal(card.Bounds.Width - 52, title.FitWidth, 3);

        title.Stop();
        var t = TimeSpan.FromSeconds(500);
        title.Now = () => t;
        Assert.True(chaster.Note("attention").Booked);
        Dispatcher.UIThread.RunJobs();
        title.Apply(t + TimeSpan.FromMilliseconds(100));
        Assert.Equal(0, Angle(title.Padlocks.First()), 3);   // -10 lean + the 10 deg jolt

        title.Start();
        Assert.True(title.IsRunning);
        tab.IsVisible = false;
        Assert.False(title.IsRunning);
        await Task.CompletedTask;
    }));

    private static double Angle(Control c)
    {
        var m = ((MatrixTransform)c.RenderTransform!).Matrix;
        return Math.Atan2(m.M12, m.M11) * 180 / Math.PI;
    }
}
