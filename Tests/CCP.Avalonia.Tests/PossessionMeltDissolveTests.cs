using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Controls.Fx;
using ConditioningControlPanel.Avalonia.Possession;
using ConditioningControlPanel.Services.Possession;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class PossessionMeltDissolveTests
{
    private sealed class Host(Window window, FakeFrames frames) : IAvaloniaPossessionHost
    {
        public Window? PossessionWindow => window;
        public Canvas? GhostLayer => null;
        public IFrameSource PossessionFrames => frames;
        public PossessionRuntime PossessionEffectRuntime { get; } = new(frames);
        public Point PointerPosition => default;
        public PossessionPointer PossessionPointer { get; } = new();
        public Rect? BoundsOnGhostLayer(Control element) => null;
        public IReadOnlyList<PossessionTarget> Targets => Array.Empty<PossessionTarget>();
        public bool IsUsable => true;
    }
    private sealed class Attribution : IPossessionAttribution
    {
        public Task ChargeAsync(object target, CancellationToken ct, int durationMs = 400) => Task.CompletedTask;
        public IDisposable Possess(object target) => new CancellationTokenSource();
        public void EdgePulse(double strength) { }
        public bool AnyPossessed => false;
        public void ReleaseAll() { }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void MeltTracksPointerAndRestoresEffects(bool photosafe, bool existingEffect) => AvaloniaTestDispatcher.Run(() =>
    {
        var prior = existingEffect ? new BlurEffect { Radius = 7 } : null;
        var card = new Border { Width = 100, Height = 50, Background = Brushes.Red, Effect = prior };
        var window = new Window { Width = 300, Height = 200, Content = card };
        var frames = new FakeFrames();
        window.Show(); Dispatcher.UIThread.RunJobs();
        var target = new PossessionTarget { Visual = card, Role = PossessionRole.Card, Key = "card", DisplayName = "card" };
        var context = new PossessionContext { Host = new Host(window, frames), Attribution = new Attribution(),
            Rung = PossessionRung.Melt, Intensity = PossessionIntensity.Gentle, Photosafe = photosafe,
            Rng = new Random(1), ElapsedFraction = 0, Remaining = TimeSpan.Zero, Name = (_, _) => { } };
        var effect = new MeltEffect();
        try
        {
            Assert.Contains(PossessionAvaloniaHead.CreateEffects(), e => e.Id == "melt");
            Assert.True(effect.CanApply(context, target));
            effect.ApplyAsync(context, target, CancellationToken.None).GetAwaiter().GetResult();
            window.MouseMove(new Point(150, 100)); Dispatcher.UIThread.RunJobs();
            frames.Frame(TimeSpan.FromMilliseconds(1000)); frames.Frame(TimeSpan.FromMilliseconds(1900));
            var group = Assert.IsType<TransformGroup>(card.RenderTransform);
            var move = Assert.IsType<TranslateTransform>(group.Children.Last());
            Assert.Equal(photosafe ? 3 : 6, move.Y, 3);
            if (prior != null) Assert.Same(prior, card.Effect);
            else Assert.Equal(photosafe ? 1 : 2, Assert.IsType<BlurEffect>(card.Effect).Radius, 3);
            window.MouseMove(new Point(5, 5)); Dispatcher.UIThread.RunJobs();
            frames.Frame(TimeSpan.FromMilliseconds(2000)); frames.Frame(TimeSpan.FromMilliseconds(2600));
            Assert.Equal(0, move.Y, 3);
            window.MouseMove(new Point(150, 100)); Dispatcher.UIThread.RunJobs();
            frames.Frame(TimeSpan.FromMilliseconds(2700)); frames.Frame(TimeSpan.FromMilliseconds(3600));
            Assert.True(move.Y > 0);
            effect.UndoAsync(TimeSpan.Zero).GetAwaiter().GetResult();
            Assert.Same(prior, card.Effect);
            Assert.False(PossessionLease.IsLeased(card));
            Assert.Equal(0, frames.Count);
            Assert.False(target.IsLive);
        }
        finally { effect.UndoAsync(TimeSpan.Zero).GetAwaiter().GetResult(); window.Close(); }
    });
    [Theory]
    [InlineData(100, 300)]
    [InlineData(600, 600)]
    [InlineData(5000, 900)]
    public Task TimedUndoEasesHomeAndPanicCanInterrupt(int requestedMs, int actualMs) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var card = new Border { Width = 100, Height = 50, Background = Brushes.Red };
        var original = new TranslateTransform(2, 3);
        card.RenderTransform = original;
        var window = new Window { Width = 300, Height = 200, Content = card };
        var frames = new FakeFrames();
        window.Show(); Dispatcher.UIThread.RunJobs();
        var target = new PossessionTarget { Visual = card, Role = PossessionRole.Card, Key = "card", DisplayName = "card" };
        var context = new PossessionContext { Host = new Host(window, frames), Attribution = new Attribution(),
            Rung = PossessionRung.Melt, Intensity = PossessionIntensity.Gentle, Photosafe = false,
            Rng = new Random(1), ElapsedFraction = 0, Remaining = TimeSpan.Zero, Name = (_, _) => { } };
        var effect = new MeltEffect();
        try
        {
            await effect.ApplyAsync(context, target, CancellationToken.None);
            window.MouseMove(new Point(150, 100)); Dispatcher.UIThread.RunJobs();
            frames.Frame(TimeSpan.FromMilliseconds(1000)); frames.Frame(TimeSpan.FromMilliseconds(1900));
            var group = Assert.IsType<TransformGroup>(card.RenderTransform);
            var move = Assert.IsType<TranslateTransform>(group.Children.Last());
            var scale = Assert.IsType<ScaleTransform>(group.Children[1]);
            var skew = Assert.IsType<SkewTransform>(group.Children[2]);
            var blur = Assert.IsType<BlurEffect>(card.Effect);
            var returning = effect.UndoAsync(TimeSpan.FromMilliseconds(requestedMs));
            Assert.False(returning.IsCompleted);
            Assert.Equal(6, move.Y, 3);
            frames.Frame(TimeSpan.FromMilliseconds(2000));
            frames.Frame(TimeSpan.FromMilliseconds(2000 + actualMs / 2d));
            Assert.InRange(move.Y, 2.9, 3.1);
            Assert.InRange(scale.ScaleY, 0.959, 0.961);
            Assert.InRange(skew.AngleY, 2.9, 3.1);
            Assert.InRange(blur.Radius, 0.99, 1.01);
            Assert.False(returning.IsCompleted);
            if (requestedMs == 600)
            {
                await effect.UndoAsync(TimeSpan.Zero);
                await returning;
            }
            else
            {
                frames.Frame(TimeSpan.FromMilliseconds(2000 + actualMs));
                await returning;
            }
            Assert.Same(original, card.RenderTransform);
            Assert.Null(card.Effect);
            Assert.False(PossessionLease.IsLeased(card));
            Assert.False(target.IsLive);
            Assert.Equal(0, frames.Count);
        }
        finally { await effect.UndoAsync(TimeSpan.Zero); window.Close(); }
    });

}
