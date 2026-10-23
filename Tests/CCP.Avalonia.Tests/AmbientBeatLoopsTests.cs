using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Descent;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// Lag fix (P82, PR #2224): ambient loops ride the window's 30 fps beat (<see cref="BeatLoop"/>), never
/// an infinite Avalonia Animation, which kept the whole window composing at render rate. Each moved
/// loop is stepped on a <see cref="SteppedTime"/> and must actually move, on WPF's curve.
/// </summary>
public sealed class AmbientBeatLoopsTests
{
    // ---- the ratchet ---------------------------------------------------------------------------

    /// <summary>Files that still run an infinite Animation, queued for lag-fixes-b. Shrink only:
    /// a file NOT in this list that gains one fails, and so does a listed file that lost it.</summary>
    private static readonly string[] NotYetMoved =
    {
        "Views/Controls/AttentionCheckControl.axaml.cs",
        "Views/Controls/Companion/ChatThresholdView.axaml",
        "Views/Controls/Companion/CompanionHeroCard.axaml",
        "Views/Controls/SeasonRecapCard.axaml.cs",
        "Views/Tabs/BlinkTrainerTabView.axaml",
        "Views/Windows/QuizWindow.axaml",
        "Views/Windows/SplashScreen.axaml",
        "Views/Windows/TeaseRevealPopup.axaml.cs",
        "Views/Windows/WebcamLoadingSplash.axaml",
    };

    private static readonly Regex Infinite = new(
        @"IterationCount\s*(\.|=\s*""?\s*)\s*Infinite|IterationType\s*\.\s*Infinite|AnimationIterationBehavior\s*\.\s*Forever",
        RegexOptions.IgnoreCase);
    private static readonly Regex CsComments = new(@"//[^\n]*|/\*.*?\*/", RegexOptions.Singleline);
    private static readonly Regex XmlComments = new(@"<!--.*?-->", RegexOptions.Singleline);

    [Fact]
    public void NoNewInfiniteAnimationInTheHead() =>
        Ratchet(Scan(code => Infinite.IsMatch(code), axaml: true), NotYetMoved,
            "An infinite Animation composes its whole window at render rate; use BeatLoop");

    /// <summary>Files whose code-behind still sets or reads a visual's Effect (lag-fixes-b, P82 rules 2-3). An
    /// Effect makes its visual an offscreen layer re-blurred on every frame anything under it moves; a glow is a
    /// sibling BoxShadow layer, a fixed blur is baked once. Shrink only.</summary>
    private static readonly string[] CodeBehindEffects =
    {
        "Controls/HoverBubbleBar.cs", "Controls/PremiumGateFx.cs", "Views/Chaos/ChaosHudWindow.axaml.cs",
        "Views/Chaos/ChaosOverlayWindow.axaml.cs", "Views/Chaos/ChaosSlotPickerWindow.axaml.cs",
        "Views/Controls/EmiDock.axaml.cs",
        "Views/Dialogs/AwarenessPresetDetailDialog.axaml.cs", "Views/Dialogs/ColorEditorDialog.axaml.cs",
        "Views/Dialogs/TubeFitDialog.axaml.cs", "Views/Dialogs/WardrobeEditorDialog.axaml.cs",
        "Views/Features/FeatureCard.axaml.cs", "Views/Lab/GazeMinigame/GazeMinigameWindow.axaml.cs",
        "Views/Tabs/AchievementsTabView.axaml.cs", "Views/Tabs/AchievementsTabView.Fx.cs",
        "Views/Tabs/LockdownTabView.axaml.cs", "Views/Tabs/QuestsTabView.Fx.cs", "Views/Tabs/StudioTabView.axaml.cs",
        "Views/Windows/AnnouncementPopup.axaml.cs", "Views/Windows/FeatureIntroPopup.axaml.cs",
        "Views/Windows/ItemUnlockedPopup.axaml.cs", "Views/Windows/LauncherWindow.Fx.cs",
        "Views/Windows/LockCardWindow.axaml.cs", "Views/Windows/MainShellWindow.Animations.cs",
        "Views/Windows/MainShellWindow.Lockdown.cs", "Views/Windows/MainShellWindow.NavRail.cs",
        "Views/Windows/MainShellWindow.TabFxTakeoverLabStatus.cs", "Views/Windows/MantraWindow.axaml.cs",
        "Views/Windows/SettingsPaletteWindow.axaml.cs", "Views/Windows/TeaseRevealPopup.axaml.cs",
    };

    /// <summary>The same rule for markup: .axaml files that still declare an Effect. Shrink only.</summary>
    private static readonly string[] XamlEffects =
    {
        "Views/AvatarTube/AvatarTubeWindow.axaml", "Views/Chaos/ChaosHubWindow.axaml", "Views/Chaos/ChaosOverlayWindow.axaml",
        "Views/Chaos/ChaosSlotPickerWindow.axaml", "Views/Controls/AttentionCheckControl.axaml",
        "Views/Controls/Companion/ChatThresholdView.axaml", "Views/Controls/Companion/CompanionHeroCard.axaml",
        "Views/Controls/Companion/MakeHerYoursView.axaml", "Views/Controls/Companion/RelationshipConstellation.axaml",
        "Views/Controls/Companion/Themes/CompanionTheme.axaml", "Views/Controls/DailyQuestCard.axaml", "Views/Controls/EmiDock.axaml",
        "Views/Controls/SeasonRecapCard.axaml", "Views/Dialogs/AttentionTargetEditorDialog.axaml",
        "Views/Dialogs/ProfilePrivacyDialog.axaml", "Views/Dialogs/RoadmapStepPopup.axaml",
        "Views/Lab/GazeMinigame/GazeMinigameWindow.axaml", "Views/Tabs/AchievementsTabView.axaml",
        "Views/Tabs/DiscordTabView.axaml", "Views/Tabs/ExclusivesTabView.axaml", "Views/Tabs/LeaderboardTabView.axaml",
        "Views/Tabs/LockdownTabView.axaml", "Views/Tabs/PlayTabView.axaml", "Views/Tabs/QuestsTabView.axaml",
        "Views/Tabs/SettingsTabView.axaml", "Views/Windows/AchievementPopup.axaml", "Views/Windows/AnnouncementPopup.axaml",
        "Views/Windows/BubbleCountResultWindow.axaml", "Views/Windows/BubbleCountWindow.axaml", "Views/Windows/FeatureIntroPopup.axaml",
        "Views/Windows/LauncherWindow.axaml", "Views/Windows/LockCardWindow.axaml", "Views/Windows/MainShellWindow.axaml",
        "Views/Windows/MantraWindow.axaml", "Views/Windows/PinkRushPopup.axaml", "Views/Windows/PopQuizWindow.axaml",
        "Views/Windows/ProgramsIntroPopup.axaml", "Views/Windows/QuestCompletePopup.axaml", "Views/Windows/QuizCategoryEditorWindow.axaml",
        "Views/Windows/QuizReportWindow.axaml", "Views/Windows/QuizWindow.axaml", "Views/Windows/SettingsPaletteWindow.axaml",
        "Views/Windows/SplashScreen.axaml", "Views/Windows/TeaseRevealPopup.axaml", "Views/Windows/TutorialOverlay.axaml",
        "Views/Windows/WebcamCalibrationWindow.axaml", "Views/Windows/WebcamGazeTrackerWindow.axaml",
        "Views/Windows/WebcamQuickRecalWindow.axaml", "Views/Windows/WhatMovedCard.axaml",
    };

    internal static readonly Regex EffectInXaml = new(@"\.Effect>|\sEffect\s*=\s*""");

    [Fact]
    public void NoNewEffectInXaml() =>
        Ratchet(Scan(code => EffectInXaml.IsMatch(code), axaml: true, cs: false), XamlEffects,
            "An Effect in markup is re-blurred on every frame anything under it moves; bake it (GlowBake) or use a sibling BoxShadow (P82)");

    internal static readonly Regex EffectInCode = new(@"\.Effect\s*=(?!=)|Effect\s+is\s+\w*Effect|\.Effect\s+as\s+");

    [Fact]
    public void NoNewEffectInCodeBehind() =>
        Ratchet(Scan(code => EffectInCode.IsMatch(code), axaml: false), CodeBehindEffects,
            "An Effect is re-blurred on every frame anything under it moves; use a sibling BoxShadow layer (P82)");

    /// <summary>Files that still drive frames from their own DispatcherTimer (renderers and loops not yet on the
    /// shared beat). A new ambient loop rides BeatLoop: one 30 fps beat per window, parked while hidden. Shrink only.</summary>
    private static readonly string[] OwnFrameTimers =
    {
        "Controls/DescentFuseRailChip.cs", "Controls/Fx/FrameClock.cs",   // FrameClock: its own off-window fallback
        "Controls/HoverPop.cs", "Controls/PremiumSpark.cs", "Controls/TakeoverOrb.cs", "Controls/VatGlassCanvas.cs",
        "Views/Overlays/FlashOverlayWindow.cs",   // its own overlay window: no beat to share, and the exit must close it even unrendered
        "Views/AvatarTube/AvatarTubeWindow.Chat.cs", "Views/Chaos/ChaosHudWindow.axaml.cs", "Views/Chaos/ChaosOverlayWindow.axaml.cs",
        "Views/Controls/SeasonRecapCard.axaml.cs", "Views/Lab/GazeMinigame/GazeMinigameWindow.axaml.cs",
        "Views/Overlays/AttentionTestTarget.cs", "Views/Overlays/FlashOverlay.cs", "Views/Overlays/SpiralOverlay.cs",
        "Views/Tabs/AchievementsTabView.Fx.cs", "Views/Tabs/ChasterTabView.Ladder.cs", "Views/Tabs/LockdownTabView.axaml.cs",
        "Views/Tabs/StudioTabView.Fx.cs", "Views/Windows/EmiDesk/EmiBookWindow.axaml.cs", "Views/Windows/LauncherWindow.Fx.cs",
        "Views/Windows/LauncherWindow.Veil.cs", "Views/Windows/MainShellWindow.Lockdown.cs", "Views/Windows/MantraWindow.axaml.cs",
        "Views/Windows/QuizWindow.axaml.cs", "Views/Windows/WebcamCalibrationWindow.axaml.cs",
    };

    internal static readonly Regex FrameRate = new(@"1000(\.0)?\s*/\s*[A-Za-z0-9_(]|FromMilliseconds\(\s*(16|33|40|41|42|50)\s*\)");

    [Fact]
    public void NoNewFrameTimerLoop() =>
        Ratchet(Scan(code => code.Contains("DispatcherTimer") && FrameRate.IsMatch(code), axaml: false), OwnFrameTimers,
            "A per-view frame timer composes off the shared beat; use BeatLoop");

    private static List<string> Scan(Func<string, bool> hit, bool axaml, bool cs = true)
    {
        var head = System.IO.Path.Combine(RepoRoot(), "CCP.Avalonia");
        var hits = new List<string>();
        foreach (var path in Directory.EnumerateFiles(head, "*.*", SearchOption.AllDirectories))
        {
            bool isCs = path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase);
            if (isCs ? !cs : !(axaml && path.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase))) continue;
            var sep = System.IO.Path.DirectorySeparatorChar;
            if (path.Contains($"{sep}obj{sep}") || path.Contains($"{sep}bin{sep}")) continue;
            var code = (isCs ? CsComments : XmlComments).Replace(File.ReadAllText(path), "");
            if (hit(code)) hits.Add(System.IO.Path.GetRelativePath(head, path).Replace('\\', '/'));
        }
        return hits;
    }

    private static void Ratchet(List<string> hits, string[] known, string why)
    {
        var added = hits.Except(known).ToList();
        Assert.True(added.Count == 0, why + ": " + string.Join(", ", added));
        var gone = known.Except(hits).ToList();
        Assert.True(gone.Count == 0, "Moved off - remove from the ratchet list: " + string.Join(", ", gone));
    }

    [Fact]
    public void TheScanSeesEverySpelling()
    {
        Assert.Matches(Infinite, "IterationCount = IterationCount.Infinite,");
        Assert.Matches(Infinite, "<Animation Duration=\"0:0:4\" IterationCount=\"INFINITE\">");
        Assert.Matches(Infinite, "new IterationCount(0, IterationType.Infinite)");
        Assert.Matches(Infinite, "breath.IterationBehavior = AnimationIterationBehavior.Forever;");
        Assert.DoesNotMatch(Infinite, "IterationCount = new IterationCount(3),");
        Assert.Matches(EffectInCode, "part.Effect = new DropShadowEffect();");
        Assert.Matches(EffectInCode, "if (TxtSeasonTitle.Effect is DropShadowEffect glow)");
        Assert.DoesNotMatch(EffectInCode, "if (part.Effect == null)");
        Assert.Matches(FrameRate, "TimeSpan.FromMilliseconds(1000.0 / 24)");
        Assert.Matches(FrameRate, "Interval = TimeSpan.FromMilliseconds(16)");
        Assert.Matches(EffectInXaml, "<TextBlock.Effect>");
        Assert.Matches(EffectInXaml, "<Border Effect=\"{StaticResource Glow}\"/>");
        Assert.DoesNotMatch(FrameRate, "TimeSpan.FromMilliseconds(160)");
    }

    private static string RepoRoot([CallerFilePath] string here = "") =>
        System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(here)!, "..", ".."));

    // ---- the loops -----------------------------------------------------------------------------

    private static T F<T>(object o, string name) =>
        (T)o.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(o)!;

    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    private static void Step(SteppedTime time, double seconds)
    {
        time.Advance(TimeSpan.FromSeconds(seconds));
        BeatLoop.StepAll();
    }

    [Fact]
    public Task ControlsBreatheOnTheBeat() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var s = CoreSettings.Current;
        var (motion, perf) = (s.MotionLevel, s.PerformanceMode);
        var time = new SteppedTime();
        BeatLoop.Time = time;
        Window? w = null;
        try
        {
            s.MotionLevel = MotionLevel.Full;
            s.PerformanceMode = false;
            var chip = new ChasterRailChip();
            var glyph = new SpiralGlyph { Width = 40, Height = 40 };
            var badge = new TierBadge { Tier = 2, Width = 60, Height = 30 };
            w = new Window { Content = new StackPanel { Children = { chip, glyph, badge } }, Width = 400, Height = 400 };
            w.Show();
            w.Activate();
            Dispatcher.UIThread.RunJobs();
            Assert.True(chip.Breathing && glyph.IsBreathing && badge.IsAnimating, "a loop never started - the test proves nothing");

            // Chaster chip (WPF ChasterRailChip.cs:645-665): glow 0.35 -> 0.8 over 2.2 s SineEase InOut,
            // the padlock still until 5.6 s then swings to -9 at 5.78 s (it never swung before: RunAsync on the Canvas).
            Assert.Equal(0.35 / 0.8, chip.GlowOpacity, 3);
            Assert.Null(F<Border>(chip, "_glowHost").Effect);
            Step(time, 2.2);
            Assert.Equal(1.0, chip.GlowOpacity, 3);
            Assert.Equal(0, chip.SwingAngle, 3);
            Step(time, 3.58);
            Assert.Equal(-9, chip.SwingAngle, 3);
            Step(time, 0.22);
            Assert.Equal(7, chip.SwingAngle, 3);
            Step(time, 1.0);                                   // 7.0 s: one lap, back at rest
            Assert.Equal(0, chip.SwingAngle, 3);

            // Spiral glyph and tier badge: the scale breathes (both were inert: RunAsync on a transform).
            var glyphScale = F<ScaleTransform>(glyph, "_breath").ScaleX;
            var badgeScale = F<ScaleTransform>(badge, "_tierScale").ScaleX;
            var badgeAngle = F<RotateTransform>(badge, "_tierRotate").Angle;
            var badgeGlow = badge.TierGlow.Opacity;
            Assert.Null(badge.TierSign.Effect);                // the glow is a sibling BoxShadow (P82), not an Effect
            Assert.True(badge.TierGlow.IsVisible);
            Assert.NotEqual(default, badge.TierGlow.BoxShadow);
            Step(time, 0.7);
            Assert.NotEqual(glyphScale, F<ScaleTransform>(glyph, "_breath").ScaleX);
            Assert.NotEqual(badgeScale, F<ScaleTransform>(badge, "_tierScale").ScaleX);
            Assert.NotEqual(badgeAngle, F<RotateTransform>(badge, "_tierRotate").Angle);
            Assert.NotEqual(badgeGlow, badge.TierGlow.Opacity);

            // Minimised or hidden: the beat parks - its clock unsubscribes, so the window requests no
            // frames (WPF renders nothing for a minimised window); shown again, it wakes.
            var idle = F<BeatLoop>(chip, "_idle");
            Assert.True(idle.IsTicking);
            w.WindowState = WindowState.Minimized;
            Dispatcher.UIThread.RunJobs();
            Assert.True(idle.Parked);
            Assert.False(idle.IsTicking);
            Assert.True(idle.IsRunning);
            w.WindowState = WindowState.Normal;
            Dispatcher.UIThread.RunJobs();
            Assert.True(idle.IsTicking);
            chip.IsVisible = false;
            Assert.False(idle.IsTicking);
            chip.IsVisible = true;
            Assert.True(idle.IsTicking);
        }
        finally
        {
            w?.Close();
            Dispatcher.UIThread.RunJobs();
            BeatLoop.Time = TimeProvider.System;
            (s.MotionLevel, s.PerformanceMode) = (motion, perf);
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task ShellLoopsBreatheOnTheBeat() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var old = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        CoreSettings.Current.MotionLevel = MotionLevel.Full;
        CoreSettings.Current.PerformanceMode = false;
        var time = new SteppedTime();
        BeatLoop.Time = time;
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            shell.Activate();
            Dispatcher.UIThread.RunJobs();

            // Descent fuse spark (WPF MotionFx.GlowBreath(glyph, 0.45, 1.0, 3.8)).
            shell.ApplyFusePhase(DescentFusePhase.Whisper);
            var spark = shell.Named<Control>("FuseSparkGlyph")!;
            Assert.Equal(0.45, spark.Opacity, 3);
            Step(time, 1.9);
            Assert.Equal(0.725, spark.Opacity, 3);
            Step(time, 1.9);
            Assert.Equal(1.0, spark.Opacity, 3);
            shell.ApplyFusePhase(DescentFusePhase.Dark);
            Assert.Equal(1.0, spark.Opacity);

            // Lockdown plate (WPF MainWindow.Animations.cs:99-112): #FF1493 -> #FF69B4, blur 12 -> 22, linear 1.5 s.
            CoreSettings.Current.MotionLevel = MotionLevel.Reduced;      // WPF :88 AllowAmbientLoops gate
            shell.StartLockdownPulse();
            Assert.False(shell.LockdownPulsing);
            CoreSettings.Current.MotionLevel = MotionLevel.Full;
            shell.StartLockdownPulse();
            Assert.True(shell.LockdownPulsing);
            var plate = shell.Named<Control>("LockdownTab")!.FindControl<Border>("LockdownImageBorder")!;
            Step(time, 1.5);
            Assert.Equal(Color.FromRgb(0xFF, 0x69, 0xB4), ((ISolidColorBrush)plate.BorderBrush!).Color);
            Assert.Equal(22, ((DropShadowEffect)plate.Effect!).BlurRadius, 3);
            Step(time, 1.5);
            Assert.Equal(Color.FromRgb(0xFF, 0x14, 0x93), ((ISolidColorBrush)plate.BorderBrush!).Color);
            shell.StopLockdownPulse();
            Assert.False(shell.LockdownPulsing);

            // Deeper wave glyph (WPF DeeperFx :141-147): drifts while the tab shows; it was inert.
            shell.ShowTab("deeper");
            Dispatcher.UIThread.RunJobs();
            if (shell.DeeperGlyphDrifting)
            {
                var drift = (TranslateTransform)shell.Named<Control>("DeeperTab")!.FindControl<Control>("DeeperWaveGlyph")!.RenderTransform!;
                double y = drift.Y;
                Step(time, 2);
                Assert.NotEqual(y, drift.Y);
                shell.ShowTab("settings");
                Dispatcher.UIThread.RunJobs();
                Assert.False(shell.DeeperGlyphDrifting);
                Assert.Equal(0, drift.Y);
            }
            else Assert.Fail("the Deeper drift never started - the test proves nothing");

            // Status dot glow (WPF TabFxTakeoverLabStatus): 0.22 -> 0.95 over 2.8 s SineEase InOut.
            shell.ShowTab("awareness");
            Dispatcher.UIThread.RunJobs();
            shell.SetAwarenessStatusPulse(true);
            var dot = shell.Named<Control>("AwarenessTab")!.FindControl<Control>("AwarenessStatusDot")!;
            var glow = Assert.IsType<DropShadowEffect>(dot.Effect);
            Assert.Equal(0.22, glow.Opacity, 3);
            Step(time, 2.8);
            Assert.Equal(0.95, glow.Opacity, 3);
            shell.SetAwarenessStatusPulse(false);
            Assert.Null(dot.Effect);
        }
        finally
        {
            shell.Close();
            Dispatcher.UIThread.RunJobs();
            BeatLoop.Time = TimeProvider.System;
            CoreSettings.ServiceProvider = old;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public void RoadmapActiveStepPulsesOnTheBeat() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        var time = new SteppedTime();
        BeatLoop.Time = time;
        var host = new Window { Width = 1200, Height = 900, Content = new QuestsTabView() };
        try
        {
            host.Show();
            var view = (QuestsTabView)host.Content!;
            view.FindControl<Button>("BtnQuestSubRoadmap")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            // WPF MainWindow.Roadmap.cs:251-260: 0.5 -> 1.0 over 0.8 s, AutoReverse, linear.
            Assert.Equal(0.5, view.RoadmapPulseOpacity!.Value, 3);
            Step(time, 0.4);
            Assert.Equal(0.75, view.RoadmapPulseOpacity!.Value, 3);
            Step(time, 0.8);
            Assert.Equal(0.75, view.RoadmapPulseOpacity!.Value, 3);   // on the way back down
            // A hidden Quests tab parks the pulse's clock (P01/P07).
            var pulse = F<BeatLoop>(view, "_pulse");
            Assert.True(pulse.IsTicking);
            view.IsVisible = false;
            Assert.False(pulse.IsTicking);
        }
        finally
        {
            host.Close();
            Dispatcher.UIThread.RunJobs();
            BeatLoop.Time = TimeProvider.System;
        }
    });

    [Fact]
    public Task SpiralTabLoopsRideTheBeat() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var s = CoreSettings.Current;
        var (motion, perf) = (s.MotionLevel, s.PerformanceMode);
        var time = new SteppedTime();
        BeatLoop.Time = time;
        Window? w = null;
        try
        {
            s.MotionLevel = MotionLevel.Full;
            s.PerformanceMode = false;
            var tab = new SpiralTabView();
            w = new Window { Content = tab, Width = 900, Height = 700 };
            w.Show();
            Dispatcher.UIThread.RunJobs();
            void Call(string name, params object[] args) =>
                typeof(SpiralTabView).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(tab, args);

            // The splash (WPF SpiralTabView.xaml.cs:1151-1184): a 4.6 s turn, halo 0.08 <-> 0.30 over 2.2 s
            // (MotionFx.GlowBreath), dots 0.15 -> 0.95 at 34% of 1.45 s, the second 0.42 s late.
            Call("ShowSplash");
            var glyph = F<global::Avalonia.Controls.Shapes.Path>(tab, "_splashGlyph");
            var spin = Assert.IsType<RotateTransform>(glyph.RenderTransform);
            var dot2 = F<TextBlock>(tab, "_splashDot2");
            double dot2Before = dot2.Opacity;
            Step(time, 0.4);
            Assert.Equal(dot2Before, dot2.Opacity, 3);           // still inside its BeginTime
            Step(time, 1.45 * 0.34 - 0.4);
            Assert.Equal(0.95, F<TextBlock>(tab, "_splashDot1").Opacity, 3);
            Step(time, 2.3 - 1.45 * 0.34);
            Assert.Equal(180, spin.Angle, 3);
            Step(time, 2.2 - 2.3 + 2.2);                          // t = 4.4: halo back at its floor
            Assert.Equal(0.08, F<global::Avalonia.Controls.Shapes.Ellipse>(tab, "_splashHalo").Opacity, 3);
            Call("HideSplash", false);
            Step(time, 1.0);
            Assert.Null(glyph.RenderTransform);                   // cancelled: the loop is gone, not parked

            // The waiting room: glow 0.18 <-> 0.70 over 3.1 s, motes rise linearly and fade in to 42% (WPF :1407-1438).
            Call("StartWaitingAmbience");
            var glow = F<Image>(tab, "_waitingGlow");
            // Both glows are baked bitmaps; no live Effect is left anywhere under the tab's loops (P82 rule 3).
            Assert.IsAssignableFrom<global::Avalonia.Media.Imaging.Bitmap>(glow.Source);
            F<TextBlock>(tab, "_fogDigits").Text = "12:34";                 // the digits' glow is re-baked per text
            Assert.IsAssignableFrom<global::Avalonia.Media.Imaging.Bitmap>(F<Image>(tab, "_fogDigitsGlow").Source);
            Assert.DoesNotContain(global::Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(tab), v => v.Effect != null);
            Assert.Equal(0.18, glow.Opacity, 3);
            Step(time, 5.6 * 0.42);
            var mote = F<global::Avalonia.Controls.Canvas>(tab, "_waitingMotes").Children.OfType<Ellipse>().First();
            Assert.Equal(0.5, mote.Opacity, 3);
            Assert.Equal(-190 * 0.42, Assert.IsType<TranslateTransform>(mote.RenderTransform).Y, 3);
            Step(time, 3.1 - 5.6 * 0.42);
            Assert.Equal(0.70, glow.Opacity, 3);
            Call("StopWaitingAmbience");
            Step(time, 1.0);
            Assert.Equal(0, mote.Opacity, 3);
            Assert.Equal(0.5, glow.Opacity, 3);
        }
        finally
        {
            w?.Close();
            (s.MotionLevel, s.PerformanceMode) = (motion, perf);
            Dispatcher.UIThread.RunJobs();
            BeatLoop.Time = TimeProvider.System;
        }
        return Task.CompletedTask;
    });
}
