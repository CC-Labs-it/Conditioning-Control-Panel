using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using Xunit;
using AvApp = global::ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>The four WPF FX decorators on their real hosts, from the shell: PremiumGateFx on a
/// premium gate (ShowTab -> RefreshEntitlementVeils), the CTA's CardSheenAdorner, the Studio
/// active tile's PerimeterCometAdorner (ShowTab("studio") -> OnTabShown) and the Presets session
/// row's RowSweepAdorner (a real pointer move). Time is a stepped clock; frames are Tick().</summary>
public sealed class GateFxAdornerTests
{
    [Fact]
    public Task PremiumGateWearsFogGlowAndCtaSheen_AndParksWhenHidden() => Run(shell =>
    {
        shell.ShowTab("lockdown");                                      // no daily-free key: always gated
        Settle(shell);
        var gate = shell.Named<Control>("LockdownTab")!.FindControl<Border>("LockdownGate")!;
        Assert.True(gate.IsVisible);                                   // free tier: padlocked
        var fx = PremiumGateFx.For(gate)!;                             // decorated by the shell's RefreshPremiumGate
        Assert.Same(fx.Fog, ((Grid)gate.Child!).Children[0]);           // fog sits behind the content
        Assert.Equal(AmbientFxLayers.FogDrift, fx.Fog.Layers);
        Assert.True(fx.IsRunning);
        Assert.True(fx.HasLockGlow);
        Assert.True(fx.GlowTicking);

        var sheen = fx.CtaSheen!;
        Assert.True(sheen.IsTicking);
        double rest = sheen.BandOffset;
        Clock.Now += TimeSpan.FromSeconds(0.65).Ticks;                  // mid-travel
        sheen.Tick();
        Assert.True(sheen.BandOffset > rest + 0.5, $"band at {sheen.BandOffset}");
        Clock.Now += TimeSpan.FromSeconds(2).Ticks;                     // into the rest
        sheen.Tick();
        Assert.Equal(rest, sheen.BandOffset, 6);

        shell.ShowTab("presets");                                       // the tab hides, the clocks park
        Settle(shell);
        Assert.False(fx.IsRunning);
        Assert.False(fx.HasLockGlow);
        Assert.Null(fx.CtaSheen);
        Assert.False(sheen.IsTicking);

        CoreSettings.Current.MotionLevel = MotionLevel.Reduced;         // reduced motion: nothing starts
        shell.ShowTab("lockdown");
        Settle(shell);
        Assert.False(fx.IsRunning);
        Assert.Null(fx.CtaSheen);

        CoreSettings.Current.MotionLevel = MotionLevel.Full;            // torn out of the tree: everything parks (P01)
        fx.Refresh();
        Assert.True(fx.IsRunning);
        ((Panel)gate.Parent!).Children.Remove(gate);
        Settle(shell);
        Assert.False(fx.IsRunning);
        Assert.False(fx.GlowTicking);
        Assert.Null(fx.CtaSheen);
    });

    [Fact]
    public Task CtaSheenHoldsTheTravelEndpointUntilTheDiscreteReset() => Run(shell =>
    {
        shell.ShowTab("lockdown");
        Settle(shell);
        var gate = shell.Named<Control>("LockdownTab")!.FindControl<Border>("LockdownGate")!;
        var sheen = Assert.IsType<CardSheenAdorner>(PremiumGateFx.For(gate)!.CtaSheen);
        sheen.Start();
        long start = Clock.Now;
        // WPF Controls/CardSheenAdorner.cs:106-112: linear travel, 50 ms hold,
        // discrete reset, then rest until the next twelve-second cycle.
        foreach (var (seconds, shift) in new[]
        {
            (0.65, 0.70), (1.30, 1.40), (1.349, 1.40), (1.35, 0.0),
            (11.999, 0.0), (12.0, 0.0), (12.65, 0.70)
        })
        {
            Clock.Now = start + TimeSpan.FromSeconds(seconds).Ticks;
            sheen.Tick();
            Assert.Equal(-0.17 + shift, sheen.BandOffset, 6);
        }
        sheen.Stop();
        Assert.Equal(-0.17, sheen.BandOffset, 6);
        Assert.False(sheen.IsTicking);
    });

    [Fact]
    public Task StudioActiveTileWearsALappingComet_ThatMovesParksAndDegrades() => Run(shell =>
    {
        shell.ShowTab("studio");
        Settle(shell);
        var rack = shell.StudioRack!;
        var comet = rack.CometFor(rack.SelectedRackKey)!;
        Assert.True(comet.IsLapping);
        Clock.Now += TimeSpan.FromSeconds(1).Ticks;                     // a quarter of the 4s lap
        comet.Tick();
        Assert.Equal(0.25, comet.Phase, 3);

        string other = rack.SelectedRackKey == "spiral" ? "flash" : "spiral";
        rack.FocusRackEntry(other);
        Settle(shell);
        Assert.Null(rack.CometFor(other == "spiral" ? "flash" : "spiral"));
        var moved = rack.CometFor(other)!;
        Assert.True(moved.IsLapping);
        Assert.False(comet.IsTicking);                                  // the old one is off its layer

        shell.ShowTab("awareness");                                     // the door closes: the clock parks
        Settle(shell);
        Assert.False(moved.IsTicking);
        Assert.False(moved.IsLapping);

        CoreSettings.Current.MotionLevel = MotionLevel.Reduced;         // degrade, never vanish
        shell.ShowTab("studio");
        Settle(shell);
        Assert.Same(moved, rack.CometFor(other));
        Assert.False(moved.IsLapping);
        Assert.True(moved.IsVisible);
    });

    [Fact]
    public Task SessionRowSweepsInOnHoverAndOutOnLeave() => Run(shell =>
    {
        shell.ShowTab("presets");
        Settle(shell);
        var tab = shell.Named<PresetsTabView>("PresetsTab")!;
        var row = tab.FindControl<StackPanel>("SessionRackPanel")!.Children.OfType<Border>().First();
        var centre = row.TranslatePoint(new Point(row.Bounds.Width / 2, row.Bounds.Height / 2), shell)!.Value;

        shell.MouseMove(centre);
        Settle(shell);
        var sweep = tab.RowSweep!;
        Assert.True(sweep.IsTicking);
        Clock.Now += TimeSpan.FromMilliseconds(110).Ticks;              // half of 220ms, eased out
        sweep.Tick();
        Assert.Equal(0.75, sweep.Progress, 3);
        Clock.Now += TimeSpan.FromMilliseconds(200).Ticks;
        sweep.Tick();
        Assert.Equal(1.0, sweep.Progress, 3);
        Assert.False(sweep.IsTicking);                                  // no clock at rest

        shell.MouseMove(new Point(2, 2));                               // leave
        Settle(shell);
        Clock.Now += TimeSpan.FromMilliseconds(170).Ticks;
        sweep.Tick();
        Assert.Equal(0.0, sweep.Progress, 3);

        CoreSettings.Current.MotionLevel = MotionLevel.Off;             // snaps, still lands
        shell.MouseMove(centre);
        Settle(shell);
        Assert.Equal(1.0, tab.RowSweep!.Progress, 3);
        Assert.False(tab.RowSweep.IsTicking);
    });

    [Fact]   // Fluent icons L0a: a converted gate (IconGlyph padlock) keeps its glow end to end.
    public Task GateWithAnIconGlyphPadlockGlows() => Run(_ =>
    {
        var glyph = new IconGlyph { Kind = FluentIcons.Common.Symbol.LockClosed, Variant = FluentIcons.Common.IconVariant.Filled, Size = 40 };
        var gate = new Border { Child = new StackPanel { Children = { glyph, new Button { Content = "Unlock" } } } };
        var host = new Window { Width = 400, Height = 300, Content = gate };
        try
        {
            host.Show(); Settle(host);
            var fx = PremiumGateFx.Attach(gate)!;
            Settle(host);
            Assert.True(fx.IsRunning);
            Assert.True(fx.HasLockGlow);
            Assert.IsType<DropShadowEffect>(glyph.Effect);              // the glow sits on the IconGlyph
        }
        finally { host.Close(); }
    });

    [Fact]
    public Task SelectedPresetSheenMovesToSessionAndParksOnHideAndMotionGate() => Run(shell =>
    {
        shell.ShowTab("presets"); Settle(shell);
        var tab = shell.Named<PresetsTabView>("PresetsTab")!;
        var preset = tab.FindControl<WrapPanel>("PresetCardsPanel")!.Children.OfType<Border>().First(c => c.Tag is string);
        var point = preset.TranslatePoint(new Point(preset.Bounds.Width / 2, preset.Bounds.Height / 2), shell)!.Value;
        shell.MouseMove(point); shell.MouseDown(point, global::Avalonia.Input.MouseButton.Left); shell.MouseUp(point, global::Avalonia.Input.MouseButton.Left);
        Settle(shell);
        Assert.Equal(preset.Tag, tab.SelectedSheenHost?.Tag);
        var first = Assert.IsType<CardSheenAdorner>(tab.SelectedCardSheen);
        Assert.True(first.IsTicking);
        Clock.Now += TimeSpan.FromSeconds(0.65).Ticks; first.Tick();
        Assert.True(first.BandOffset > 0.5);

        var session = tab.FindControl<StackPanel>("SessionRackPanel")!.Children.OfType<Border>().First();
        point = session.TranslatePoint(new Point(session.Bounds.Width / 2, session.Bounds.Height / 2), shell)!.Value;
        shell.MouseMove(point); shell.MouseDown(point, global::Avalonia.Input.MouseButton.Left); shell.MouseUp(point, global::Avalonia.Input.MouseButton.Left);
        Settle(shell);
        Assert.Same(session, tab.SelectedSheenHost);
        Assert.False(first.IsTicking);
        var second = Assert.IsType<CardSheenAdorner>(tab.SelectedCardSheen);
        shell.ShowTab("awareness"); Settle(shell);
        Assert.Null(tab.SelectedCardSheen); Assert.False(second.IsTicking);
        shell.ShowTab("presets"); Settle(shell);
        Assert.NotNull(tab.SelectedCardSheen);
        CoreSettings.Current.MotionLevel = MotionLevel.Reduced;
        AmbientFxCanvas.Env.RaiseMotionGateChanged();
        Assert.Null(tab.SelectedCardSheen);
    });

    [Fact]
    public Task ChasterHeroSheenTravelsAndParksOnHideDetachAndMotionGate() => Run(shell =>
    {
        shell.ShowTab("chaster"); Settle(shell);
        var tab = shell.Named<ChasterTabView>("ChasterTab")!;
        var hero = tab.FindControl<Border>("HeroCard")!;
        var layer = global::Avalonia.Controls.Primitives.AdornerLayer.GetAdornerLayer(hero);
        Assert.True(tab.HeroSheen != null, $"tab visible={tab.IsVisible}, effective={tab.IsEffectivelyVisible}, gate={AmbientFxCanvas.Env.AllowAmbientLoops}, hero={hero.IsEffectivelyVisible}, layer={layer}");
        var sheen = Assert.IsType<CardSheenAdorner>(tab.HeroSheen);
        Assert.True(sheen.IsTicking);
        Clock.Now += TimeSpan.FromSeconds(0.65).Ticks; sheen.Tick();
        Assert.True(sheen.BandOffset > 0.5);
        shell.ShowTab("presets"); Settle(shell);
        Assert.Null(tab.HeroSheen); Assert.False(sheen.IsTicking);
        shell.ShowTab("chaster"); Settle(shell);
        Assert.NotNull(tab.HeroSheen);
        CoreSettings.Current.MotionLevel = MotionLevel.Off;
        AmbientFxCanvas.Env.RaiseMotionGateChanged();
        Assert.Null(tab.HeroSheen);
        CoreSettings.Current.MotionLevel = MotionLevel.Full;
        AmbientFxCanvas.Env.RaiseMotionGateChanged();
        sheen = Assert.IsType<CardSheenAdorner>(tab.HeroSheen);
        ((Panel)tab.Parent!).Children.Remove(tab); Settle(shell);
        Assert.Null(tab.HeroSheen); Assert.False(sheen.IsTicking);
    });

    private static readonly SteppedClock Clock = new();

    private static void Settle(Window w) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }

    private static Task Run(Action<MainShellWindow> body) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var s = CoreSettings.Current;
        var old = (s.MotionLevel, s.PerformanceMode);
        var oldPremium = CoreEntitlement.HasPremiumProvider;
        FxAdorner.Time = Clock;
        MainShellWindow? shell = null;
        try
        {
            (s.MotionLevel, s.PerformanceMode) = (MotionLevel.Full, false);
            CoreEntitlement.HasPremiumProvider = null;                  // free tier: gates up
            shell = new MainShellWindow { Width = 1400, Height = 900 };
            shell.Show(); Settle(shell);
            body(shell);
        }
        finally
        {
            shell?.Close();
            FxAdorner.Time = TimeProvider.System;
            CoreEntitlement.HasPremiumProvider = oldPremium;
            (s.MotionLevel, s.PerformanceMode) = old;
        }
        return Task.CompletedTask;
    });

    private sealed class SteppedClock : TimeProvider
    {
        public long Now;
        public override long GetTimestamp() => Now;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    }
}
