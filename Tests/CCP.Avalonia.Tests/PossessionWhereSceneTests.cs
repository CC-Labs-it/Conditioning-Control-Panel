using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Controls.Fx;
using ConditioningControlPanel.Avalonia.Possession;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Possession;
using Xunit;

namespace CCP.Avalonia.Tests;

public class PossessionWhereSceneTests
{
    private sealed class Host : IAvaloniaPossessionHost
    {
        public Window? PossessionWindow => null;
        public Canvas? GhostLayer => null;
        public FakeFrames Frames { get; } = new();
        public IFrameSource PossessionFrames => Frames;
        public PossessionRuntime PossessionEffectRuntime => new(Frames);
        public Point PointerPosition => default;
        public PossessionPointer PossessionPointer { get; } = new();
        public Rect? BoundsOnGhostLayer(Control element) => null;
        public IReadOnlyList<PossessionTarget> Targets => Array.Empty<PossessionTarget>();
        public bool IsUsable => true;
    }
    private sealed class Attribution : IPossessionAttribution
    {
        public bool AnyPossessed { get; private set; }
        public Task ChargeAsync(object target, CancellationToken ct, int durationMs = 400) => Task.CompletedTask;
        public IDisposable Possess(object target) { AnyPossessed = true; return new Handle(() => AnyPossessed = false); }
        public void EdgePulse(double strength) { }
        public void ReleaseAll() => AnyPossessed = false;
        private sealed class Handle(Action dispose) : IDisposable { public void Dispose() => dispose(); }
    }
    [Fact]
    public void PhotosafeHalvesSagAndCancellationRestores() => AvaloniaTestDispatcher.Run(() =>
    {
        var host = new Host(); var attribution = new Attribution(); var card = new Button();
        var target = new PossessionTarget { Visual = card, Role = PossessionRole.Card, Key = "card" };
        var ctx = new PossessionContext { Host = host, Attribution = attribution, Rung = PossessionRung.Melt,
            Intensity = PossessionIntensity.Eerie, Photosafe = true, Rng = new Random(7), ElapsedFraction = 0,
            Remaining = TimeSpan.Zero, Name = (_, _) => { } };
        var scene = new WhereYouAreScene();
        using var cts = new CancellationTokenSource();
        _ = scene.RunAsync(ctx, host, _ => target, cts.Token);
        host.Frames.Frame(TimeSpan.Zero); host.Frames.Frame(TimeSpan.FromMilliseconds(2600));
        var group = Assert.IsType<TransformGroup>(card.RenderTransform);
        Assert.Equal(3.5, group.Children.OfType<TranslateTransform>().Single().Y, 3);
        cts.Cancel();
        Assert.Equal(0, host.Frames.Count);
        Assert.False(attribution.AnyPossessed);
        Assert.Equal(Matrix.Identity, card.RenderTransform?.Value ?? Matrix.Identity);
    });

    [Fact]
    public void SceneBreathesSagsAndPanicRestoresImmediately() => AvaloniaTestDispatcher.Run(() =>
    {
        var host = new Host(); var attribution = new Attribution(); var card = new Button();
        var original = new RotateTransform(12); card.RenderTransform = original;
        var target = new PossessionTarget { Visual = card, Role = PossessionRole.Card, Key = "card", DisplayName = "card" };
        string? named = null;
        var ctx = new PossessionContext { Host = host, Attribution = attribution, Rung = PossessionRung.Melt,
            Intensity = PossessionIntensity.Eerie, Photosafe = false, Rng = new Random(7), ElapsedFraction = 0,
            Remaining = TimeSpan.Zero, Name = (id, _) => named = id };
        var scene = Assert.IsType<WhereYouAreScene>(Assert.Single(PossessionAvaloniaHead.CreateDirectorHead().Scenes()));
        var run = scene.RunAsync(ctx, host, role => role == PossessionRole.Card ? target : null, CancellationToken.None);
        Assert.Equal("scene_where_you_are", named);
        Assert.True(attribution.AnyPossessed);
        host.Frames.Frame(TimeSpan.Zero); host.Frames.Frame(TimeSpan.FromMilliseconds(700));
        var group = Assert.IsType<TransformGroup>(card.RenderTransform);
        Assert.Equal(1.035, group.Children.OfType<ScaleTransform>().Single().ScaleX, 3);
        host.Frames.Frame(TimeSpan.FromMilliseconds(2600));
        Assert.Equal(7, group.Children.OfType<TranslateTransform>().Single().Y, 3);
        PanicSurfaces.All.Single(s => s.Id == "possession").Stop(null);
        Assert.Same(original, card.RenderTransform);
        Assert.False(attribution.AnyPossessed);
        Assert.Equal(0, host.Frames.Count);
        host.Frames.Frame(TimeSpan.FromSeconds(5));
        Assert.Same(original, card.RenderTransform);
    });
}
