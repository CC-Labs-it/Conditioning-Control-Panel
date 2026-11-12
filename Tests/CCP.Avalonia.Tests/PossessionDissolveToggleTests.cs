using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Controls.Fx;
using ConditioningControlPanel.Avalonia.Possession;
using ConditioningControlPanel.Services.Possession;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class PossessionDissolveToggleTests
{
    private sealed class Host(Window window, Canvas layer, FakeFrames frames) : IAvaloniaPossessionHost
    {
        public Window? PossessionWindow => window;
        public Canvas? GhostLayer => layer;
        public IFrameSource PossessionFrames => frames;
        public PossessionRuntime PossessionEffectRuntime { get; } = new(frames);
        public Point PointerPosition => default;
        public Rect? BoundsOnGhostLayer(Control element) => null;
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
    public void NamedKnobMovesAndUndoRestoresWithoutWritingChecked(bool checkedValue) => AvaloniaTestDispatcher.Run(() =>
    {
        var original = new TranslateTransform(2, 0);
        var knob = new Border { Name = "knob", Width = 16, Height = 16, RenderTransform = original };
        var toggle = new ToggleButton { Width = 100, Height = 30, IsChecked = checkedValue, Opacity = 0.7,
            Template = new FuncControlTemplate<ToggleButton>((_, _) => new Canvas { Children = { knob } }) };
        var layer = new Canvas { IsHitTestVisible = false };
        var window = new Window { Width = 200, Height = 100, Content = new Grid { Children = { toggle, layer } } };
        var frames = new FakeFrames();
        window.Show(); Dispatcher.UIThread.RunJobs();
        var target = new PossessionTarget { Visual = toggle, Role = PossessionRole.Toggle, Key = "switch", DisplayName = "switch" };
        var ctx = new PossessionContext { Host = new Host(window, layer, frames), Attribution = new Attribution(),
            Rung = PossessionRung.Melt, Intensity = PossessionIntensity.Eerie, Photosafe = false,
            Rng = new Random(1), ElapsedFraction = 0, Remaining = TimeSpan.Zero, Name = (_, _) => { } };
        var effect = new ToggleLieEffect();
        int writes = 0;
        toggle.PropertyChanged += (_, e) => { if (e.Property == ToggleButton.IsCheckedProperty) writes++; };
        try
        {
            Assert.Contains(PossessionAvaloniaHead.CreateEffects(), e => e.Id == "togglelie");
            Assert.True(effect.CanApply(ctx, target));
            effect.ApplyAsync(ctx, target, CancellationToken.None).GetAwaiter().GetResult();
            Assert.True(PossessionLease.IsLeased(knob));
            frames.Frame(TimeSpan.FromMilliseconds(1000));
            frames.Frame(TimeSpan.FromMilliseconds(1240));
            var group = Assert.IsType<TransformGroup>(knob.RenderTransform);
            var movement = Assert.IsType<TranslateTransform>(group.Children.Last());
            Assert.True(Math.Abs(movement.X) >= 6);
            Assert.Equal(checkedValue, toggle.IsChecked);
            Assert.Equal(0, writes);
            effect.UndoAsync(TimeSpan.Zero).GetAwaiter().GetResult();
            Assert.Same(original, knob.RenderTransform);
            Assert.False(PossessionLease.IsLeased(knob));
            Assert.Equal(0.7, toggle.Opacity);
            Assert.True(toggle.IsVisible);
            Assert.Equal(checkedValue, toggle.IsChecked);
            Assert.Equal(0, writes);
            Assert.False(target.IsLive);
            frames.Frame(TimeSpan.FromMilliseconds(1500));
            Assert.Same(original, knob.RenderTransform);
        }
        finally { effect.UndoAsync(TimeSpan.Zero).GetAwaiter().GetResult(); window.Close(); }
    });
}
