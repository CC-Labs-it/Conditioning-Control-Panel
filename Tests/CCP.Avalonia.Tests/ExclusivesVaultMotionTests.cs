using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using Xunit;
using AvApp = global::ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>WPF MainWindow.Exclusives.cs motion + chrome: tier plates, livery, teasers, breath, pulse,
/// Ken Burns and sheens, reached through the shell's ShowTab and parked when the tab hides (P01).</summary>
public sealed class ExclusivesVaultMotionTests
{
    private static readonly SteppedClock Clock = new();

    [Fact]
    public Task VaultChromeAndMotionFollowTheTabAndTheGates() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var s = CoreSettings.Current;
        var old = (s.MotionLevel, s.PerformanceMode);
        var (premium, lab, free) = (CoreEntitlement.HasPremiumProvider, CoreEntitlement.HasLabProvider, CoreEntitlement.IsFreeTodayProvider);
        FxAdorner.Time = Clock;
        MainShellWindow? shell = null;
        var bark = CoreBark.TabNavigated;
        try
        {
            (s.MotionLevel, s.PerformanceMode) = (MotionLevel.Full, false);
            CoreEntitlement.HasPremiumProvider = null;
            CoreEntitlement.HasLabProvider = null;
            CoreEntitlement.IsFreeTodayProvider = null;
            shell = new MainShellWindow { Width = 1400, Height = 900 };
            shell.Show(); Settle();
            // WPF polish 12: "exclusives" lands on Home > Premium silently; barks still hear the old key.
            var heard = new System.Collections.Generic.List<string?>();
            CoreBark.TabNavigated = heard.Add;
            shell.ShowTab("exclusives"); Settle();
            CoreBark.TabNavigated = bark;
            Assert.Equal("premium", shell.CurrentTab);
            Assert.Equal(new[] { "exclusives" }, heard);
            var view = shell.Named<ExclusivesTabView>("ExclusivesTab")!;
            Assert.True(view.IsVisible);
            Assert.True(view.MotionRunning);

            // Free account: both tier plates dim, none breathes.
            var p1 = view.FindControl<Border>("TierPlate1")!;
            Assert.Null(view.LitPlate);
            Assert.Equal(0.3, p1.Opacity);
            Step(view, 1.7);

            // Basic, Prime, Free headers (the reserved seats are gone); a shown padlock glows; tiered cards wear the 3px living livery.
            var items = view.FindControl<ItemsControl>("ExclusivesShelf")!.Items;
            Assert.Equal(new[] { "basic", "prime", "free" }, items.OfType<ExclusiveGroupRow>().Select(g => g.Group.ToString().ToLowerInvariant()));
            var mark = Parts(view, "VeilLock").First(l => l.IsEffectivelyVisible);
            Assert.Null(mark.Effect);   // the glow is a sibling BoxShadow layer (P82)
            Assert.True(Lit(mark));
            var tiered = Parts(view, "ExCard").Cast<Border>().First(b => ((ExclusiveCardRow)b.DataContext!).Tier > 0);
            Assert.True(TierFxBorder.GetTier(tiered) > 0);
            Assert.Equal(new Thickness(3), tiered.BorderThickness);
            Assert.Equal(ExclusiveFeature.All[0].Tier, TierFxBorder.GetTier(view.FindControl<Border>("SpotlightCard")!));
            Assert.True(view.SheenCount > 0);
            // Only padlocks under a shown veil glow (WPF ApplyVeilLockBreath(lock, veil visible)).
            var locks = Parts(view, "VeilLock");
            Assert.Contains(locks, l => l.IsEffectivelyVisible && Lit(l));
            Assert.Contains(locks, l => !l.IsEffectivelyVisible);
            Assert.All(locks.Where(l => !l.IsEffectivelyVisible), l => Assert.False(Lit(l)));

            // FREE TODAY pulse (fade + swell). No roster entry reaches the untiered gold pill today (fyp,
            // the spotlight, re-stamps its badge instead), so the hero pill is shown by hand.
            var pill = view.FindControl<Border>("SpotFreeToday")!;
            pill.IsVisible = true;
            Step(view, 1.7);
            Assert.True(pill.Opacity < 1);
            Assert.True(((ScaleTransform)pill.RenderTransform!).ScaleX > 1);

            // Another tab: every loop parks and the room rests.
            var kept = view.MotionParts;
            Assert.NotNull(kept.Loop);
            Assert.NotNull(kept.Sheen);
            shell.ShowTab("settings"); Settle();
            Assert.False(view.MotionRunning);
            Assert.Equal(0, view.SheenCount);
            Assert.False(kept.Loop!.IsTicking);   // parked clocks really stop, not just hide
            Assert.False(kept.Sheen!.IsTicking);
            Assert.False(Lit(mark));
            Assert.Equal(1, pill.Opacity);
            Assert.Equal(1, ((ScaleTransform)pill.RenderTransform!).ScaleX);

            // Back with Basic access: plate I lights and breathes 0.75..1.0.
            CoreEntitlement.HasPremiumProvider = () => true;
            shell.ShowTab("premium"); Settle();
            Assert.True(view.MotionRunning);
            // perf-exclusives-p95: a show wakes the parked loop and sheens; it does not rebuild them.
            Assert.Same(kept.Loop, view.MotionParts.Loop);
            Assert.Same(kept.Sheen, view.MotionParts.Sheen);
            Assert.True(view.SheenCount > 0);
            // A show starts afresh like WPF: the zoom from 1.0 and every sheen sweeping on entry.
            Step(view, 0);
            Assert.Equal(1, ((ScaleTransform)view.FindControl<Image>("SpotArtImage")!.RenderTransform!).ScaleX);
            double rest = kept.Sheen.BandOffset;
            Clock.Now += (long)(0.65 * TimeSpan.TicksPerSecond);
            kept.Sheen.Tick();
            Assert.True(kept.Sheen.BandOffset > rest);
            Assert.Same(p1, view.LitPlate);
            Assert.Equal(0.3, view.FindControl<Border>("TierPlate2")!.Opacity);
            Step(view, 3.4 * 2 + 1.7 - 0.65);   // 0.65 s already spent on the sheen above
            Assert.InRange(p1.Opacity, 0.87, 0.88);

            // Reduced motion: no clock, the lit plate rests at full.
            s.MotionLevel = MotionLevel.Reduced;
            AmbientFxCanvas.Env.RaiseMotionGateChanged();
            Assert.False(view.MotionRunning);
            Assert.Equal(1, p1.Opacity);

            // The Lab bar lights plate II instead.
            CoreEntitlement.HasLabProvider = () => true;
            shell.RefreshExclusivesTab();
            Assert.Same(view.FindControl<Border>("TierPlate2"), view.LitPlate);
            Assert.Equal(0.55, p1.Opacity);
        }
        finally
        {
            CoreBark.TabNavigated = bark;
            shell?.Close();
            FxAdorner.Time = TimeProvider.System;
            (CoreEntitlement.HasPremiumProvider, CoreEntitlement.HasLabProvider, CoreEntitlement.IsFreeTodayProvider) = (premium, lab, free);
            (s.MotionLevel, s.PerformanceMode) = old;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public void BreathIsWpfSineEaseAutoReverse()
    {
        Assert.Equal(0.35, ExclusivesTabView.Breath(0, 3.4, 0.35, 0.9), 6);
        Assert.Equal(0.9, ExclusivesTabView.Breath(3.4, 3.4, 0.35, 0.9), 6);
        Assert.Equal(0.625, ExclusivesTabView.Breath(1.7, 3.4, 0.35, 0.9), 6);
        Assert.Equal(0.35, ExclusivesTabView.Breath(6.8, 3.4, 0.35, 0.9), 6);
        Assert.Equal(0.35 + 0.55 * (1 - Math.Cos(Math.PI / 4)) / 2, ExclusivesTabView.Breath(0.85, 3.4, 0.35, 0.9), 6);   // eased, not linear
    }

    private static bool Lit(Control padlock) => ExclusivesTabView.LockGlow(padlock)!.IsVisible;

    private static Control[] Parts(Control root, string name) =>
        root.GetVisualDescendants().OfType<Control>().Where(c => c.Name == name).ToArray();

    private static void Step(ExclusivesTabView view, double seconds)
    {
        Clock.Now += (long)(seconds * TimeSpan.TicksPerSecond);
        view.MotionFrame();
    }

    private static void Settle()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private sealed class SteppedClock : TimeProvider
    {
        public long Now;
        public override long GetTimestamp() => Now;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    }
}
