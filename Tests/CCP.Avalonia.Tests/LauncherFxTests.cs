using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Launcher;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Launcher slice 5 (WPF LauncherWindow.Fx/Choreo/Backdrop/Edge/Crossfade.cs): the frame clock runs
/// only while the launcher is shown and not minimised, honours the motion setting, and hover / Play beats
/// change state on a stepped clock. Sounds go through CoreAudio, captured here, never played.</summary>
public sealed class LauncherFxTests
{
    private static void Run(MotionLevel motion, Action<MainShellWindow, LauncherWindow, List<string>> body)
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var service = new SettingsService();
            CoreSettings.ServiceProvider = () => service;
            var s = CoreSettings.Current;
            s.Welcomed = true;
            s.HasAcceptedAgeVerification = true;
            s.WhatMovedCardShown = 1;   // P52: the upgrade-only What moved card would take the real clicks
            s.MotionLevel = motion;
            s.PerformanceMode = false;
            s.MasterVolume = 50;
            s.LauncherSoundEnabled = true;
            var cues = new List<string>();
            var (oldIn, oldLab, oldAudio) = (CoreAccount.IsLoggedInProvider, CoreEntitlement.HasLabProvider, CoreAudio.PlayOneShotProvider);
            CoreAudio.PlayOneShotProvider = (_, _, tag, _, _) => cues.Add(tag);
            CoreAccount.IsLoggedInProvider = () => true;
            CoreEntitlement.HasLabProvider = () => true;
            var shell = new MainShellWindow();
            try
            {
                shell.Show();
                Dispatcher.UIThread.RunJobs();
                Assert.True(LauncherWindow.BackToLauncher(shell));
                shell.StepLauncherFade(1); // with motion on the door first fades the panel (WPF MainWindow.Launcher.cs)
                Dispatcher.UIThread.RunJobs();
                var launcher = LauncherWindow.Instance!;
                launcher.UpdateLayout();
                body(shell, launcher, cues);
            }
            finally
            {
                LauncherWindow.Instance?.Close();
                LockdownService.Current = null;
                (CoreAccount.IsLoggedInProvider, CoreEntitlement.HasLabProvider, CoreAudio.PlayOneShotProvider) = (oldIn, oldLab, oldAudio);
                shell.Close();
                CoreSettings.ServiceProvider = null;
            }
        });
    }

    /// <summary>The intake tile: the last card (LauncherCards order); game cards sit before it.</summary>
    private static Border Tile(LauncherWindow w) => (Border)w.FindControl<UniformGrid>("GamesGrid")!.Children[^1];

    private static void Step(LauncherWindow w, double seconds)
    {
        for (double t = 0; t < seconds; t += 0.02) w.StepFx(0.02);
    }

    [Fact]
    public void CtaAndWanderingTileSheensParkOnHideAndMotionGate() => Run(MotionLevel.Full, (_, launcher, _) =>
    {
        Dispatcher.UIThread.RunJobs(); launcher.UpdateLayout();
        var cta = Assert.IsType<ConditioningControlPanel.Avalonia.Controls.CardSheenAdorner>(launcher.CtaSheen);
        Assert.True(cta.IsTicking);
        Assert.True(launcher.WanderTicking);
        launcher.WanderSheenPass();
        var wander = Assert.IsType<ConditioningControlPanel.Avalonia.Controls.CardSheenAdorner>(launcher.WanderSheen);
        Assert.True(wander.IsTicking);
        launcher.BuildTiles(); Dispatcher.UIThread.RunJobs(); launcher.UpdateLayout();
        Assert.False(wander.IsTicking); Assert.Null(launcher.WanderSheen);
        Assert.Same(cta, launcher.CtaSheen); Assert.True(cta.IsTicking); Assert.True(launcher.WanderTicking);   // rebuild restarts the wander only
        CoreSettings.Current.MotionLevel = MotionLevel.Reduced;
        ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env.RaiseMotionGateChanged();
        Assert.Null(launcher.CtaSheen); Assert.False(cta.IsTicking); Assert.False(launcher.WanderTicking);
        CoreSettings.Current.MotionLevel = MotionLevel.Full;
        ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env.RaiseMotionGateChanged();
        cta = Assert.IsType<ConditioningControlPanel.Avalonia.Controls.CardSheenAdorner>(launcher.CtaSheen);
        launcher.OnFxActivated(false);
        Assert.Null(launcher.CtaSheen); Assert.False(cta.IsTicking); Assert.False(launcher.WanderTicking);
        launcher.OnFxActivated(true);
        cta = Assert.IsType<ConditioningControlPanel.Avalonia.Controls.CardSheenAdorner>(launcher.CtaSheen);
        // WPF StartWanderingSheen returns on _perfLow: the Performance tier closes the ambient gate.
        CoreSettings.Current.PerformanceMode = true;
        ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env.RaiseMotionGateChanged();
        Assert.False(launcher.WanderTicking); Assert.Null(launcher.CtaSheen);
        CoreSettings.Current.PerformanceMode = false;
        launcher.Hide();
        Assert.Null(launcher.CtaSheen); Assert.False(cta.IsTicking); Assert.False(launcher.WanderTicking);
    });

    [Fact]
    public void Clock_RunsOnlyWhileShownAndNotMinimised() => Run(MotionLevel.Full, (_, launcher, _) =>
    {
        Assert.True(launcher.FxTicking);
        Step(launcher, 0.5);
        Assert.NotEqual(0, launcher.SpiralTurn.Angle);   // the backdrop turns
        Assert.True(launcher.FxTicking);

        launcher.WindowState = WindowState.Minimized;
        Assert.False(launcher.FxTicking);
        launcher.WindowState = WindowState.Normal;
        Assert.True(launcher.FxTicking);

        launcher.Hide();
        Assert.False(launcher.FxTicking);
        launcher.StepFx(0.1);                            // a stray tick on a hidden launcher parks, never runs
        Assert.False(launcher.FxTicking);
    });

    [Fact]
    public void MotionOff_StopsTheClock_AndHoverSnaps() => Run(MotionLevel.Off, (shell, launcher, _) =>
    {
        launcher.StepFx(0.05);
        Assert.False(launcher.FxTicking);
        Assert.Equal(0, launcher.SpiralTurn.Angle);
        Assert.Equal(1, launcher.FindControl<Panel>("RootGrid")!.Opacity);

        var tile = Tile(launcher);
        launcher.TileHover(tile, true);
        Assert.Equal(1.02, ((TransformGroup)tile.RenderTransform!).Children.OfType<ScaleTransform>().Single().ScaleX, 3);

        // Play hides at once: no beat is held without motion.
        tile.GetVisualDescendants().OfType<Button>().First(b => !Equals(b.Tag, "tile-shortcut")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.False(launcher.IsVisible);
        Assert.True(shell.IsVisible);
    });

    [Fact]
    public void ReducedMotion_KeepsTransitions_ButNoAmbientSpin() => Run(MotionLevel.Reduced, (_, launcher, _) =>
    {
        Step(launcher, 0.5);
        Assert.Equal(0, launcher.SpiralTurn.Angle);      // ambient loops are Full-only
        Assert.True(launcher.FxTicking);                 // the edge still breathes (WPF gates it on transitions)
    });

    [Fact]
    public void Hover_LiftsGlowsAndBleeds_ThenSettles() => Run(MotionLevel.Full, (_, launcher, cues) =>
    {
        var tile = Tile(launcher);
        var scale = ((TransformGroup)tile.RenderTransform!).Children.OfType<ScaleTransform>().Single();
        var glow = launcher.FindControl<Border>("GlowLayer")!;
        launcher.TileHover(tile, true);
        Assert.Contains("launcher-hover", cues);
        Step(launcher, 0.5);
        Assert.Equal(1.02, scale.ScaleX, 3);
        Assert.Null(tile.Effect);
        Assert.Equal(0.7, launcher.TileGlow(tile)!.Opacity, 3);
        Assert.Equal(0.45, glow.Opacity, 3);

        launcher.TileHover(tile, false);
        Step(launcher, 0.5);
        Assert.Equal(1, scale.ScaleX, 3);
        Assert.Equal(0.28, glow.Opacity, 3);
    });

    [Fact]
    public void Play_HoldsTheHideForTheBeat_ShakesDimsAndFades() => Run(MotionLevel.Full, (shell, launcher, cues) =>
    {
        Assert.Contains("launcher-open", cues);
        Step(launcher, 0.4);                             // the fade-in lands
        var card = launcher.FindControl<Border>("PanelCard")!;
        var dimmedTile = launcher.FindControl<UniformGrid>("GamesGrid")!.Children.OfType<Border>().First(t => t != Tile(launcher));
        Tile(launcher).GetVisualDescendants().OfType<Button>().First(b => !Equals(b.Tag, "tile-shortcut")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Contains("launcher-launch", cues);
        Assert.True(launcher.IsVisible);                 // held for the beat

        launcher.StepFx(0.05);
        Assert.NotEqual(0, launcher.ShakeSlide.X);
        Step(launcher, 0.3);
        Assert.Equal(0.55, card.Opacity, 2);             // the rest stepped back
        Assert.Equal(0.55, dimmedTile.Opacity, 2);
        var dimmedGlow = launcher.TileGlow(dimmedTile)!;   // inside the tile, so it dims with it as the Effect did
        Assert.True(dimmedTile.IsVisualAncestorOf(dimmedGlow));
        Assert.Equal(0.35, dimmedGlow.Opacity, 3);
        Assert.True(launcher.IsVisible);

        Step(launcher, 0.3);                             // FadeLeadMs + FadeOutMs = 450 ms
        Dispatcher.UIThread.RunJobs();
        Assert.False(launcher.IsVisible);
        Assert.True(shell.IsVisible);
        Assert.Equal(1, launcher.FindControl<Panel>("RootGrid")!.Opacity);
    });

    [Fact]
    public void Sounds_RespectTheLauncherSpeaker() => Run(MotionLevel.Full, (_, launcher, cues) =>
    {
        cues.Clear();
        CoreSettings.Current.LauncherSoundEnabled = false;
        launcher.TileHover(Tile(launcher), true);
        launcher.FindControl<Button>("BtnMinimize")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Empty(cues);
        CoreSettings.Current.LauncherSoundEnabled = true;
        launcher.WindowState = WindowState.Normal;
        launcher.FindControl<Button>("BtnMinimize")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(new[] { "launcher-click" }, cues);
    });

    /// <summary>WPF LauncherWindow.Sound.cs: a real click on the title-bar speaker mutes in silence,
    /// repaints (slash, dim cone, unmute tooltip); unmuting answers with one click.</summary>
    [Fact]
    public void SpeakerButton_ByRealClick_MutesQuietly_UnmutesWithANote() => Run(MotionLevel.Full, (_, launcher, cues) =>
    {
        var btn = launcher.FindControl<Button>("BtnSound")!;
        var slash = launcher.FindControl<global::Avalonia.Controls.Shapes.Path>("SoundSlash")!;
        void Press()
        {
            var p = btn.TranslatePoint(new Point(btn.Bounds.Width / 2, btn.Bounds.Height / 2), launcher)!.Value;
            launcher.MouseDown(p, MouseButton.Left);
            launcher.MouseUp(p, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
        }
        Assert.False(slash.IsVisible);
        Assert.Equal(Loc.Get("launcher_sound_mute"), ToolTip.GetTip(btn));
        cues.Clear();
        Press();
        Assert.False(CoreSettings.Current.LauncherSoundEnabled);
        Assert.True(slash.IsVisible);
        Assert.False(launcher.FindControl<global::Avalonia.Controls.Shapes.Path>("SoundWaves")!.IsVisible);
        Assert.Equal(Loc.Get("launcher_sound_unmute"), ToolTip.GetTip(btn));
        Assert.Empty(cues);
        Press();
        Assert.True(CoreSettings.Current.LauncherSoundEnabled);
        Assert.False(slash.IsVisible);
        Assert.Equal(new[] { "launcher-click" }, cues);
    });

    private sealed class SteppedUtc : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.UtcNow.AddDays(1);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public void Hover_ByRealPointer_PlaysTheThrottledMelody() => Run(MotionLevel.Full, (_, launcher, cues) =>
    {
        var clock = new SteppedUtc();
        LauncherWindow.LauncherSfx.Clock = clock;
        try
        {
            var tile = Tile(launcher);
            var scale = ((TransformGroup)tile.RenderTransform!).Children.OfType<ScaleTransform>().Single();
            var centre = tile.TranslatePoint(new Point(tile.Bounds.Width / 2, tile.Bounds.Height / 2), launcher)!.Value;
            cues.Clear();
            launcher.MouseMove(centre);
            Step(launcher, 0.4);
            Assert.Equal(1.02, scale.ScaleX, 3);
            launcher.MouseMove(new Point(2, 2));
            launcher.MouseMove(centre);                  // re-entered inside MinGapMs: no second note
            Assert.Single(cues, c => c == "launcher-hover");
            clock.Now += TimeSpan.FromMilliseconds(LauncherMelody.MinGapMs + 1);
            launcher.MouseMove(new Point(2, 2));
            launcher.MouseMove(centre);
            Assert.Equal(2, cues.Count(c => c == "launcher-hover"));
        }
        finally { LauncherWindow.LauncherSfx.Clock = TimeProvider.System; }
    });

    [Fact]
    public void Play_OffScreen_IsSilent() => Run(MotionLevel.Full, (shell, launcher, cues) =>
    {
        launcher.Hide();                                 // a game boot / handoff plays with the launcher off screen
        cues.Clear();
        launcher.Play(LauncherCards.Find("intake")!);
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(cues);
        Assert.True(shell.IsVisible);
    });

    /// <summary>WPF LauncherHost.cs:360-366: a pending leash punishment sends a game tile to the gate on
    /// the panel instead of the game.</summary>
    [Fact]
    public void Play_UnderALeashPunishment_GoesToTheGate() => Run(MotionLevel.Full, (shell, launcher, _) =>
    {
        var (oldDue, oldGate) = (MainShellWindow.LeashGateDueProvider, MainShellWindow.PresentLeashGateProvider);
        int gates = 0;
        MainShellWindow.LeashGateDueProvider = () => true;
        MainShellWindow.PresentLeashGateProvider = _ => gates++;
        try
        {
            launcher.Hide();                             // off screen: no exit beat to wait out
            launcher.Play(LauncherCards.Find("intake")!);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, gates);
            Assert.NotEqual("gradedintake", shell.CurrentTab);
        }
        finally { (MainShellWindow.LeashGateDueProvider, MainShellWindow.PresentLeashGateProvider) = (oldDue, oldGate); }
    });

    [Fact]
    public void Deactivated_ParksTheSpirals() => Run(MotionLevel.Full, (_, launcher, _) =>
    {
        Step(launcher, 0.3);
        // Headless never moves activation between windows; drive the Deactivated/Activated handlers.
        launcher.OnFxActivated(false);
        double parked = launcher.SpiralTurn.Angle;
        Step(launcher, 0.5);
        Assert.Equal(parked, launcher.SpiralTurn.Angle);
        launcher.OnFxActivated(true);
        Step(launcher, 0.5);
        Assert.NotEqual(parked, launcher.SpiralTurn.Angle);
    });

    [Fact]
    public void Close_DropsAPendingExit() => Run(MotionLevel.Full, (shell, launcher, _) =>
    {
        Tile(launcher).GetVisualDescendants().OfType<Button>().First(b => !Equals(b.Tag, "tile-shortcut")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(launcher.IsVisible);                 // held for the beat
        launcher.Close();
        Dispatcher.UIThread.RunJobs();
        Assert.False(shell.IsVisible);                   // the held step never ran
    });

    // ------------------------------------------------------------------ the edge (WPF LauncherWindow.Edge.cs)

    private static void Frames(LauncherWindow w, int n)
    {
        for (int i = 0; i < n; i++) w.StepFx(0.02);
    }

    /// <summary>Hide + Show restarts the open cue with no dispatcher turn in between, so no real tick runs.</summary>
    private static (Border Glow, Border Glint) Reopen(LauncherWindow w)
    {
        w.Hide();
        w.Show();
        return (w.FindControl<Border>("EdgeGlow")!, w.FindControl<Border>("EdgeGlint")!);
    }

    [Fact]
    public void Edge_FlaresBreathesAndGlints_OnWpfTiming() => Run(MotionLevel.Full, (_, launcher, _) =>
    {
        var (glow, glint) = Reopen(launcher);
        Frames(launcher, 7);                              // FlareEdge: 0.85 at 140 ms
        Assert.Equal(0.85, glow.Opacity, 2);
        Assert.Equal(0.9 * 140 / 250, glint.Opacity, 2);  // RunGlint: fades in over 250 ms
        Frames(launcher, 18);                             // 500 ms: the glint holds 0.9
        Assert.Equal(0.9, glint.Opacity, 2);
        Frames(launcher, 20);                             // 900 ms: back to rest
        Assert.Equal(0.3, glow.Opacity, 2);
        Frames(launcher, 31);                             // 1.52 s: the glint is gone
        Assert.Equal(0, glint.Opacity);
        Frames(launcher, 130);                            // breath (from 0.92 s): 0.30 -> 0.44 over 3.2 s
        Assert.Equal(0.44, glow.Opacity, 2);
        Frames(launcher, 484);                            // 13.8 s: no idle glint yet
        Assert.Equal(0, glint.Opacity);
        Frames(launcher, 20);                             // 14.2 s: the idle glint passes (OnEdgeIdle, 14 s)
        Assert.True(glint.Opacity > 0.5, $"idle glint {glint.Opacity}");
    });

    [Fact]
    public void Edge_FocusBack_GlintsAtMostEveryFourSeconds() => Run(MotionLevel.Full, (_, launcher, cues) =>
    {
        var clock = new SteppedUtc();
        LauncherWindow.LauncherSfx.Clock = clock;
        try
        {
            var (_, glint) = Reopen(launcher);
            Frames(launcher, 80);                         // the open glint is done
            cues.Clear();
            clock.Now += TimeSpan.FromSeconds(3.9);
            launcher.OnFxActivated(true);                 // inside the 4 s gap: no second cue
            launcher.StepFx(0.1);
            Assert.DoesNotContain("launcher-edge", cues);
            Assert.Equal(0, glint.Opacity);

            clock.Now += TimeSpan.FromSeconds(0.2);       // 4.1 s since the open's cue
            launcher.OnFxActivated(true);
            launcher.StepFx(0.1);
            Assert.Contains("launcher-edge", cues);
            Assert.True(glint.Opacity > 0, "focus back runs the glint");
        }
        finally { LauncherWindow.LauncherSfx.Clock = TimeProvider.System; }
    });

    [Fact]
    public void Edge_ParksWhenMinimisedOrHidden() => Run(MotionLevel.Full, (_, launcher, _) =>
    {
        var (glow, glint) = Reopen(launcher);
        Frames(launcher, 7);
        launcher.WindowState = WindowState.Minimized;
        Assert.False(launcher.FxTicking);
        Assert.Equal(0.3, glow.Opacity);
        Assert.Equal(0, glint.Opacity);
        launcher.WindowState = WindowState.Normal;
        Frames(launcher, 7);
        launcher.Hide();
        Assert.False(launcher.FxTicking);
        Assert.Equal(0.3, glow.Opacity);
        Assert.Equal(0, glint.Opacity);
    });

    [Fact]
    public void Edge_WindowIsRounded_AndTheGlintTurnsInTheUnitBox() => Run(MotionLevel.Full, (_, launcher, _) =>
    {
        Assert.Equal(Win32Properties.WindowCornerPreference.Round, Win32Properties.GetWindowCornerPreference(launcher));
        Assert.Contains(WindowTransparencyLevel.Transparent, launcher.TransparencyLevelHint);
        Assert.Equal(0, ((ISolidColorBrush)launcher.Background!).Color.A);
        var frame = launcher.FindControl<Border>("WindowFrame")!;
        Assert.True(frame.ClipToBounds);
        Assert.Equal(new CornerRadius(8), frame.CornerRadius);
        launcher.WindowState = WindowState.Maximized;
        Assert.Equal(new CornerRadius(0), frame.CornerRadius);
        launcher.WindowState = WindowState.Normal;
        Assert.Equal(new CornerRadius(8), frame.CornerRadius);

        // WPF RelativeTransform: a quarter turn carries the band's right-middle end to the bottom-middle.
        var b = launcher.FindControl<Border>("EdgeGlint")!.Bounds;
        Assert.True(b.Width > b.Height && b.Height > 0);
        launcher.EdgeGlintSpin.Angle = 90;
        launcher.ApplyGlintSpin();
        var p = new Point(b.Width, b.Height / 2) * launcher.EdgeGlintRel.Matrix;
        Assert.Equal(b.Width / 2, p.X, 3);
        Assert.Equal(b.Height, p.Y, 3);
    });
}
