using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Controls.Fx;
using ConditioningControlPanel.Avalonia.Possession;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Possession;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class PossessionXpDrainTests
{
    private sealed class Host(Window window, Canvas layer, FakeFrames frames) : IAvaloniaPossessionHost
    {
        public Window? PossessionWindow => window;
        public Canvas? GhostLayer => layer;
        public IFrameSource PossessionFrames => frames;
        public PossessionRuntime PossessionEffectRuntime { get; } = new(frames);
        public Point PointerPosition => default;
        public PossessionPointer PossessionPointer { get; } = new();
        public Rect? BoundsOnGhostLayer(Control el) => el.TransformToVisual(layer) is { } m ? new Rect(el.Bounds.Size).TransformToAABB(m) : null;
        public IReadOnlyList<PossessionTarget> Targets => Array.Empty<PossessionTarget>();
        public bool IsUsable => true;
    }
    private sealed class Attribution : IPossessionAttribution
    {
        public Task ChargeAsync(object target, CancellationToken ct, int durationMs = 400) => Task.CompletedTask;
        public IDisposable Possess(object target) => new Release();
        public void EdgePulse(double strength) { }
        public bool AnyPossessed => false;
        public void ReleaseAll() { }
        private sealed class Release : IDisposable { public void Dispose() { } }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task DrainsAndPanicRestoresWithoutOverwritingRealLevelChange(bool levelChanged) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var bar = new Border { Name = "XPBar", Width = 100, Height = 10, Background = Brushes.Red, Opacity = 0.7 };
        var chip = new TextBlock { Name = "TxtLevelLabel", Text = "LVL 5" };
        var layer = new Canvas { IsHitTestVisible = false };
        var window = new Window { Width = 300, Height = 150, Content = new Grid { Children = { bar, chip, layer } } };
        var frames = new FakeFrames();
        var host = new Host(window, layer, frames);
        var target = new PossessionTarget { Visual = chip, Role = PossessionRole.Label, Key = "level", DisplayName = "level" };
        var context = new PossessionContext { Host = host, Attribution = new Attribution(), Rung = PossessionRung.Melt,
            Intensity = PossessionIntensity.Eerie, Photosafe = false, Rng = new Random(1), ElapsedFraction = 0,
            Remaining = TimeSpan.Zero, Name = (_, _) => { } };
        var effect = new XpDrainEffect();
        window.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            Assert.Contains(PossessionAvaloniaHead.CreateEffects(), e => e.Id == "xpdrain");
            Assert.True(effect.CanApply(context, target));
            await effect.ApplyAsync(context, target, CancellationToken.None);
            Assert.Equal(0, bar.Opacity);
            Assert.Equal("LVL 0", chip.Text);
            var ghost = Assert.IsType<Border>(Assert.Single(layer.Children));
            Assert.False(ghost.IsHitTestVisible);
            frames.Frame(TimeSpan.FromMilliseconds(1000));
            frames.Frame(TimeSpan.FromMilliseconds(1750));
            Assert.InRange(ghost.Width, 49, 51);
            frames.Frame(TimeSpan.FromMilliseconds(2500));
            Assert.Equal(0, ghost.Width);
            if (levelChanged) chip.Text = "LVL 6";
            PanicSurfaces.All.Single(s => s.Id == "possession").Stop(null);
            Assert.Equal(0.7, bar.Opacity);
            Assert.Equal(levelChanged ? "LVL 6" : "LVL 5", chip.Text);
            Assert.Empty(layer.Children);
            Assert.Equal(0, frames.Count);
            Assert.False(effect.IsLive);
            Assert.False(target.IsLive);
        }
        finally { await effect.UndoAsync(TimeSpan.Zero); window.Close(); }
    });
}
