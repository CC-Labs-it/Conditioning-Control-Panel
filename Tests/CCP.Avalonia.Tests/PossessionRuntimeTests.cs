using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Diagnostics;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Possession;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Possession;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>possession-head-a: the effect host on Avalonia controls, driven by the Core deck and a
/// stepped frame source (P08). WPF refs: Services/Possession/TransformLease.cs, PossessionDirector.cs
/// UndoAll/HoldThenUndoAsync, PossessionOffLimitsTree.cs.</summary>
public sealed class PossessionRuntimeTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().WithInterFont().With(ConditioningControlPanel.Avalonia.Platform.AppFonts.Options()).ConfigureFonts(ConditioningControlPanel.Avalonia.Platform.AppFonts.Register).UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    private static Window Host(Control content)
    {
        var w = new Window { Width = 400, Height = 300, Content = content };
        w.Show();
        Dispatcher.UIThread.RunJobs();
        return w;
    }

    private static Button Btn(string name) => new() { Name = name, Width = 80, Height = 30, Content = "x" };

    [Fact]
    public Task DeckPickedNudgeMovesItsTargetThenGivesItsOwnTransformBack() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var label = new TextBlock { Name = "TxtHello", Text = "hello", Width = 80, Height = 20 };
        var prior = new ScaleTransform(1.1, 1.1);
        label.RenderTransform = prior;
        var win = Host(new StackPanel { Children = { label } });
        try
        {
            var frames = new FakeFrames();
            var rt = new PossessionRuntime(frames, rng: new Random(7));
            // Settle + Label: the deck can only pick nudge (drift needs the Drift rung).
            Assert.True(rt.PickAndApply(PossessionRuntime.CreateAll(), new[] { new PossessionHeadTarget(label, PossessionRole.Label, "hello") },
                PossessionRung.Settle, PossessionIntensity.Eerie));
            frames.Frame(TimeSpan.FromMilliseconds(1000));
            frames.Frame(TimeSpan.FromMilliseconds(1400));   // past the 320 ms shove: held crooked
            var group = Assert.IsType<TransformGroup>(label.RenderTransform);
            var t = group.Children.OfType<TranslateTransform>().Single();
            Assert.InRange(Math.Abs(t.X), 3.0, 4.5);
            Assert.InRange(Math.Abs(t.Y), 2.0, 3.5);
            Assert.Contains(prior, group.Children);

            frames.Frame(TimeSpan.FromMilliseconds(1000 + 2000 + 300));   // halfway home
            Assert.InRange(Math.Abs(t.X), 0.01, 3.0);
            frames.Frame(TimeSpan.FromMilliseconds(1000 + 2000 + 600));   // home: released
            Assert.Same(prior, label.RenderTransform);
            Assert.Equal(BindingPriority.Unset, label.GetDiagnostic(Visual.RenderTransformOriginProperty).Priority);
            Assert.Equal(0, rt.LiveCount);
            Assert.Equal(0, frames.Count);   // the frame request stops with the last ghost
            Assert.False(PossessionLease.IsLeased(label));
        }
        finally { win.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task OffLimitsControlsAreRefusedAndNeverOffered() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var exit = Btn("BtnEmergencyExit");                       // reserved name
        var inCard = Btn("BtnInside");                            // ancestor named for the lockdown card
        var card = new Border { Name = "LockdownCardBorder", Child = inCard };
        var guarded = new StackPanel { Name = "Holder" };         // CONTAINS an excluded child
        var secret = Btn("BtnPlain");
        PossessionTree.SetExclude(secret, true);
        guarded.Children.Add(secret);
        var ok = Btn("BtnOk");
        var win = Host(new StackPanel { Children = { exit, card, guarded, ok } });
        try
        {
            var frames = new FakeFrames();
            var rt = new PossessionRuntime(frames, rng: new Random(1));
            var nudge = new NudgeHeadEffect();
            var breathe = new BreatheHeadEffect();
            Assert.False(rt.TryApply(nudge, new(exit, PossessionRole.Button, "exit")));
            Assert.False(rt.TryApply(nudge, new(inCard, PossessionRole.Button, "inCard")));
            Assert.False(rt.TryApply(nudge, new(secret, PossessionRole.Button, "secret")));
            Assert.False(rt.TryApply(breathe, new(guarded, PossessionRole.Card, "guarded")));
            foreach (var c in new Control[] { exit, inCard, secret, guarded }) Assert.IsNotType<TransformGroup>(c.RenderTransform);
            Assert.Equal(0, rt.LiveCount);

            var offLimits = new[] { new PossessionHeadTarget(exit, PossessionRole.Button, "exit"), new(inCard, PossessionRole.Button, "inCard") };
            for (int i = 0; i < 20; i++)
                Assert.False(rt.PickAndApply(PossessionRuntime.CreateAll(), offLimits, PossessionRung.Settle, PossessionIntensity.FullDoki));
            Assert.Equal(0, frames.Count);

            Assert.True(rt.TryApply(nudge, new(ok, PossessionRole.Button, "ok")));   // an ordinary button is fair game
            rt.UndoAll();
        }
        finally { win.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task PanicRestoresEveryTouchedControlAtOnce() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var label = new TextBlock { Text = "drifting", Width = 80, Height = 20 };
        var button = Btn("BtnNudge");
        var card = new Border { Width = 120, Height = 60, Background = Brushes.Gray };
        var originSet = new RelativePoint(0.2, 0.8, RelativeUnit.Relative);
        card.RenderTransformOrigin = originSet;
        var priorRot = new RotateTransform(5);
        var timer = new TextBlock { Text = "09:59", Width = 60, Height = 20, RenderTransform = priorRot };
        var win = Host(new StackPanel { Children = { label, button, card, timer } });
        try
        {
            var frames = new FakeFrames();
            var rt = new PossessionRuntime(frames, rng: new Random(3));
            Assert.True(rt.TryApply(new DriftHeadEffect(), new(label, PossessionRole.Label, "l")));
            Assert.True(rt.TryApply(new NudgeHeadEffect(), new(button, PossessionRole.Button, "b")));
            Assert.True(rt.TryApply(new BreatheHeadEffect(), new(card, PossessionRole.Card, "c")));
            Assert.True(rt.TryApply(new WobbleHeadEffect(), new(timer, PossessionRole.Timer, "t")));
            frames.Frame(TimeSpan.FromMilliseconds(500));
            frames.Frame(TimeSpan.FromMilliseconds(1700));
            Assert.All(new Control[] { label, button, card, timer }, c => Assert.IsType<TransformGroup>(c.RenderTransform));
            Assert.NotEqual(Matrix.Identity, card.RenderTransform!.Value);

            PanicSurfaces.All.Single(s => s.Id == "possession").Stop(null);

            Assert.Null(label.RenderTransform);
            Assert.Equal(BindingPriority.Style, button.GetDiagnostic(Visual.RenderTransformProperty).Priority);   // the theme's own value is back
            Assert.Equal(BindingPriority.Style, button.GetDiagnostic(Animatable.TransitionsProperty).Priority);  // and its transition
            Assert.Contains(button.Transitions!, t => t is TransitionBase b && b.Property == Visual.RenderTransformProperty);
            Assert.Null(card.RenderTransform);
            Assert.Equal(originSet, card.RenderTransformOrigin);
            Assert.Same(priorRot, timer.RenderTransform);
            Assert.Equal(BindingPriority.Unset, timer.GetDiagnostic(Visual.RenderTransformOriginProperty).Priority);
            Assert.Equal(0, rt.LiveCount);
            Assert.Equal(0, frames.Count);
        }
        finally { win.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task AControlOnAHiddenTabIsNeitherPossessedNorOffered() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var label = new TextBlock { Text = "on a tab", Width = 80, Height = 20 };
        var tab = new StackPanel { Children = { label } };
        var shown = new TextBlock { Text = "shown", Width = 80, Height = 20 };
        var win = Host(new StackPanel { Children = { tab, shown } });
        try
        {
            tab.IsVisible = false;   // the shell hides a tab through its root, never the leaf
            Dispatcher.UIThread.RunJobs();
            Assert.True(label.IsVisible);
            var frames = new FakeFrames();
            var rt = new PossessionRuntime(frames, rng: new Random(2));
            Assert.False(rt.TryApply(new NudgeHeadEffect(), new(label, PossessionRole.Label, "l")));
            Assert.False(rt.PickAndApply(PossessionRuntime.CreateAll(), new[] { new PossessionHeadTarget(label, PossessionRole.Label, "l") },
                PossessionRung.Settle, PossessionIntensity.Eerie));
            Assert.Null(label.RenderTransform);
            Assert.Equal(0, frames.Count);
            // never offered: the deck can only land on the visible sibling, whatever it rolls
            var both = new[] { new PossessionHeadTarget(label, PossessionRole.Label, "l"), new PossessionHeadTarget(shown, PossessionRole.Label, "s") };
            for (int seed = 0; seed < 20; seed++)
            {
                var r = new PossessionRuntime(new FakeFrames(), rng: new Random(seed));
                Assert.True(r.PickAndApply(PossessionRuntime.CreateAll(), both, PossessionRung.Settle, PossessionIntensity.Eerie));
                Assert.Null(label.RenderTransform);
                r.UndoAll();
            }
        }
        finally { win.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task AReleasedControlCoolsDownFor45Seconds() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var a = new TextBlock { Text = "a", Width = 80, Height = 20 };
        var b = new TextBlock { Text = "b", Width = 80, Height = 20 };
        var win = Host(new StackPanel { Children = { a, b } });
        try
        {
            var frames = new FakeFrames();
            var time = new SteppedTime();
            var rt = new PossessionRuntime(frames, time, new Random(4));
            var nudge = new NudgeHeadEffect();
            var onlyA = new[] { new PossessionHeadTarget(a, PossessionRole.Label, "a") };
            double t0 = 1000;
            void RunOut() { frames.Frame(TimeSpan.FromMilliseconds(t0)); t0 += 2700; frames.Frame(TimeSpan.FromMilliseconds(t0)); t0 += 100; }

            Assert.True(rt.PickAndApply(new HeadEffect[] { nudge }, onlyA, PossessionRung.Settle, PossessionIntensity.Eerie));
            RunOut();
            Assert.Equal(0, rt.LiveCount);
            Assert.True(rt.TryApply(nudge, new(b, PossessionRole.Label, "b")));   // so "a" is no longer the last victim
            RunOut();

            time.Advance(TimeSpan.FromSeconds(44));
            Assert.False(rt.PickAndApply(new HeadEffect[] { nudge }, onlyA, PossessionRung.Settle, PossessionIntensity.Eerie));
            time.Advance(TimeSpan.FromSeconds(2));
            Assert.True(rt.PickAndApply(new HeadEffect[] { nudge }, onlyA, PossessionRung.Settle, PossessionIntensity.Eerie));
            rt.UndoAll();
        }
        finally { win.Close(); }
        return Task.CompletedTask;
    });
}
