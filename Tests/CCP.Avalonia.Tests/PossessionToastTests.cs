using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Controls.Fx;
using ConditioningControlPanel.Avalonia.Possession;
using ConditioningControlPanel.Services.Possession;
using ConditioningControlPanel.Services.Possession.Effects;
using Xunit;

namespace CCP.Avalonia.Tests;

public class PossessionToastTests
{
    private sealed class Host(Window window, Canvas layer) : IAvaloniaPossessionHost
    {
        public Window? PossessionWindow => window;
        public Canvas? GhostLayer => layer;
        public FakeFrames Frames { get; } = new();
        public IFrameSource PossessionFrames => Frames;
        public PossessionRuntime PossessionEffectRuntime => new(Frames);
        public Point PointerPosition => default;
        public PossessionPointer PossessionPointer { get; } = new();
        public Rect? BoundsOnGhostLayer(Control c) => null;
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
    [Fact]
    public Task ToastSlidesDismissesWithoutConsumingPressAndCleansUp() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var button = new Border { Background = Brushes.Transparent, HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch, VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Stretch };
        var layer = new Canvas { IsHitTestVisible = false };
        var window = new Window { Width = 600, Height = 400, Content = new Grid { Children = { button, layer } } };
        window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        var host = new Host(window, layer);
        var ctx = new PossessionContext { Host = host, Attribution = new Attribution(), Rung = PossessionRung.Drift,
            Intensity = PossessionIntensity.Gentle, Photosafe = false, Rng = new Random(1), ElapsedFraction = 0,
            Remaining = TimeSpan.FromMinutes(1), Name = (_, _) => { } };
        var effect = new ToastEffect();
        try
        {
            Assert.Contains(PossessionAvaloniaHead.CreateEffects(), e => e.Id == "toast");
            Assert.Equal(TimeSpan.FromSeconds(4), effect.HoldFor);
            Assert.True(effect.CanApply(ctx, null));
            await effect.ApplyAsync(ctx, null, CancellationToken.None);
            var toast = Assert.IsType<Border>(Assert.Single(layer.Children));
            var row = Assert.IsType<Grid>(toast.Child);
            var text = Assert.IsType<TextBlock>(row.Children[1]);
            Assert.Contains(text.Text, ToastLines.PoolFor(null));
            var slide = Assert.IsType<TranslateTransform>(toast.RenderTransform);
            Assert.Equal(318, slide.X); Assert.Equal(0, toast.Opacity);
            host.Frames.Frame(TimeSpan.Zero); host.Frames.Frame(TimeSpan.FromMilliseconds(280));
            Assert.Equal(0, slide.X); Assert.Equal(1, toast.Opacity);
            bool received = false;
            button.PointerPressed += (_, _) => received = true;
            button.RaiseEvent(new PointerPressedEventArgs(button, new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true), layer,
                new Point(Canvas.GetLeft(toast) + 5, Canvas.GetTop(toast) + 5), 0,
                new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None));
            Assert.True(received);
            host.Frames.Frame(TimeSpan.FromMilliseconds(300)); host.Frames.Frame(TimeSpan.FromMilliseconds(380));
            Assert.InRange(slide.X, 0.01, 317.99); Assert.InRange(toast.Opacity, 0.01, .99);
            host.Frames.Frame(TimeSpan.FromMilliseconds(460));
            Assert.Empty(layer.Children);
            for (int i = 0; effect.IsLive && i < 50; i++) await Task.Delay(10);
            Assert.False(effect.IsLive);
            await effect.ApplyAsync(ctx, null, CancellationToken.None);
            Assert.Single(layer.Children);
            await effect.UndoAsync(TimeSpan.Zero);
            Assert.Empty(layer.Children); Assert.Equal(0, host.Frames.Count);
        }
        finally { await effect.UndoAsync(TimeSpan.Zero); window.Close(); }
    });
}
