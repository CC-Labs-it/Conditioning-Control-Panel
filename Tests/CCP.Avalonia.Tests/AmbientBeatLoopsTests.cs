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
        "Views/Tabs/SpiralTabView.axaml.cs",
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
    public void NoNewInfiniteAnimationInTheHead()
    {
        var head = System.IO.Path.Combine(RepoRoot(), "CCP.Avalonia");
        var hits = new List<string>();
        foreach (var path in Directory.EnumerateFiles(head, "*.*", SearchOption.AllDirectories))
        {
            bool cs = path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase);
            if (!cs && !path.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase)) continue;
            var sep = System.IO.Path.DirectorySeparatorChar;
            if (path.Contains($"{sep}obj{sep}") || path.Contains($"{sep}bin{sep}")) continue;
            var code = (cs ? CsComments : XmlComments).Replace(File.ReadAllText(path), "");
            if (Infinite.IsMatch(code)) hits.Add(System.IO.Path.GetRelativePath(head, path).Replace('\\', '/'));
        }
        var added = hits.Except(NotYetMoved).ToList();
        Assert.True(added.Count == 0, "An infinite Animation composes its whole window at render rate; use BeatLoop: " + string.Join(", ", added));
        var gone = NotYetMoved.Except(hits).ToList();
        Assert.True(gone.Count == 0, "Moved off the infinite Animation - remove from NotYetMoved: " + string.Join(", ", gone));
    }

    [Fact]
    public void TheScanSeesEverySpelling()
    {
        Assert.Matches(Infinite, "IterationCount = IterationCount.Infinite,");
        Assert.Matches(Infinite, "<Animation Duration=\"0:0:4\" IterationCount=\"INFINITE\">");
        Assert.Matches(Infinite, "new IterationCount(0, IterationType.Infinite)");
        Assert.Matches(Infinite, "breath.IterationBehavior = AnimationIterationBehavior.Forever;");
        Assert.DoesNotMatch(Infinite, "IterationCount = new IterationCount(3),");
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
            Step(time, 0.7);
            Assert.NotEqual(glyphScale, F<ScaleTransform>(glyph, "_breath").ScaleX);
            Assert.NotEqual(badgeScale, F<ScaleTransform>(badge, "_tierScale").ScaleX);
            Assert.NotEqual(badgeAngle, F<RotateTransform>(badge, "_tierRotate").Angle);

            // Minimised: every beat parks (writes nothing), like WPF rendering nothing for a minimised window.
            w.WindowState = WindowState.Minimized;
            Dispatcher.UIThread.RunJobs();
            Assert.True(F<BeatLoop>(chip, "_idle").Parked);
            w.WindowState = WindowState.Normal;
            Dispatcher.UIThread.RunJobs();
            Assert.False(F<BeatLoop>(chip, "_idle").Parked);
            chip.IsVisible = false;
            Assert.True(F<BeatLoop>(chip, "_idle").Parked);
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
                var drift = (TranslateTransform)shell.Named<Control>("DeeperTab")!.FindControl<TextBlock>("DeeperWaveGlyph")!.RenderTransform!;
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
        }
        finally
        {
            host.Close();
            Dispatcher.UIThread.RunJobs();
            BeatLoop.Time = TimeProvider.System;
        }
    });
}
