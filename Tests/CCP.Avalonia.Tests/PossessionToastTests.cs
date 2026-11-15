using System;
using System.Collections.Generic;
using System.Collections;
using System.Reflection;
using Pointer = Avalonia.Input.Pointer;
using IconKind = FluentIcons.Common.Symbol;
using IconVariant = FluentIcons.Common.IconVariant;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Controls;
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
            var applying = effect.ApplyAsync(ctx, null, CancellationToken.None);
            var toast = Assert.IsType<Border>(Assert.Single(layer.Children));
            var row = Assert.IsType<Grid>(toast.Child);
            var text = Assert.IsType<TextBlock>(row.Children[1]);
            Assert.Contains(text.Text, ToastLines.PoolFor(null));
            var slide = Assert.IsType<TranslateTransform>(toast.RenderTransform);
            Assert.Equal(318, slide.X); Assert.Equal(0, toast.Opacity);
            host.Frames.Frame(TimeSpan.Zero); host.Frames.Frame(TimeSpan.FromMilliseconds(280));
            Assert.Equal(0, slide.X); Assert.Equal(1, toast.Opacity);
            await applying;
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
            var applyingAgain = effect.ApplyAsync(ctx, null, CancellationToken.None);
            Assert.Single(layer.Children);
            await effect.UndoAsync(TimeSpan.Zero);
            await applyingAgain;
            Assert.Empty(layer.Children); Assert.Equal(0, host.Frames.Count);
        }
        finally { await effect.UndoAsync(TimeSpan.Zero); window.Close(); }
    });
    [Theory]
    [InlineData(PointerType.Mouse, PointerUpdateKind.LeftButtonPressed, RawInputModifiers.LeftMouseButton, true, false)]
    [InlineData(PointerType.Mouse, PointerUpdateKind.RightButtonPressed, RawInputModifiers.RightMouseButton, false, true)]
    [InlineData(PointerType.Mouse, PointerUpdateKind.MiddleButtonPressed, RawInputModifiers.MiddleMouseButton, true, false)]
    [InlineData(PointerType.Touch, PointerUpdateKind.LeftButtonPressed, RawInputModifiers.LeftMouseButton, true, false)]
    public Task PressInsideDismissesAndOnlyRightPressIsHandled(PointerType pointerType, PointerUpdateKind kind, RawInputModifiers modifiers, bool reachesBelow, bool handled) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var below = new Border { Background = Brushes.Transparent };
        var layer = new Canvas { IsHitTestVisible = false };
        var window = new Window { Width = 600, Height = 400, Content = new Grid { Children = { below, layer } } };
        window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        var host = new Host(window, layer);
        var ctx = new PossessionContext { Host = host, Attribution = new Attribution(), Rung = PossessionRung.Drift,
            Intensity = PossessionIntensity.Gentle, Photosafe = false, Rng = new Random(1), ElapsedFraction = 0,
            Remaining = TimeSpan.FromMinutes(1), Name = (_, _) => { } };
        var effect = new ToastEffect();
        try
        {
            var apply = effect.ApplyAsync(ctx, null, CancellationToken.None);
            var toast = Assert.IsType<Border>(Assert.Single(layer.Children));
            host.Frames.Frame(TimeSpan.Zero); host.Frames.Frame(TimeSpan.FromMilliseconds(280));
            await apply;
            bool received = false;
            below.PointerPressed += (_, _) => received = true;
            var press = new PointerPressedEventArgs(below, new Pointer(Pointer.GetNextFreeId(), pointerType, true), layer,
                new Point(Canvas.GetLeft(toast) + 5, Canvas.GetTop(toast) + 5), 0,
                new PointerPointProperties(modifiers, kind), KeyModifiers.None);
            below.RaiseEvent(press);
            Assert.Equal(reachesBelow, received); Assert.Equal(handled, press.Handled);
            host.Frames.Frame(TimeSpan.FromMilliseconds(300)); host.Frames.Frame(TimeSpan.FromMilliseconds(460));
            if (pointerType != PointerType.Mouse || kind == PointerUpdateKind.MiddleButtonPressed) { Assert.Single(layer.Children); Assert.True(effect.IsLive); }
            else Assert.Empty(layer.Children);
        }
        finally { await effect.UndoAsync(TimeSpan.Zero); window.Close(); }
    });

    [Fact]
    public Task DirectorStartsHoldOnlyAfterEntrance() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var layer = new Canvas { IsHitTestVisible = false };
        var window = new Window { Width = 600, Height = 400, Content = new Grid { Children = { layer } } };
        window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        var host = new Host(window, layer);
        var effect = new ToastEffect();
        using var lockdown = new LockdownService();
        using var director = new PossessionDirector(lockdown, new[] { effect }, new PossessionDirectorHead
        {
            Settings = () => new AppSettings { LockdownPossessionEnabled = true }, Attribution = _ => new Attribution()
        });
        director.AttachHost(host);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        try
        {
            typeof(PossessionDirector).GetMethod("OnLockdownActivated", flags)!.Invoke(director, null);
            var entries = (IList)typeof(PossessionDirector).GetField("_hosts", flags)!.GetValue(director)!;
            var start = (Task)typeof(PossessionDirector).GetMethod("StartOneAsync", flags)!
                .Invoke(director, new[] { entries[0], (object)PossessionRung.Drift, TimeSpan.FromMinutes(1), .1 })!;
            Assert.Single(layer.Children);
            host.Frames.Frame(TimeSpan.Zero); host.Frames.Frame(TimeSpan.FromMilliseconds(280));
            await Task.Delay(150);
            Assert.False(start.IsCompleted);
            await start;
            await Task.Delay(3800);
            Assert.True(effect.IsLive); Assert.Single(layer.Children);
            await Task.Delay(400);
            host.Frames.Frame(TimeSpan.FromMilliseconds(300)); host.Frames.Frame(TimeSpan.FromMilliseconds(580));
            await Task.Delay(30);
            Assert.Empty(layer.Children);
        }
        finally { director.UndoAll(); await effect.UndoAsync(TimeSpan.Zero); window.Close(); }
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task CancelOrStalledEntranceCleansUp(bool cancel) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var layer = new Canvas { IsHitTestVisible = false };
        var window = new Window { Width = 600, Height = 400, Content = new Grid { Children = { layer } } };
        window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        var host = new Host(window, layer);
        var ctx = new PossessionContext { Host = host, Attribution = new Attribution(), Rung = PossessionRung.Drift,
            Intensity = PossessionIntensity.Gentle, Photosafe = false, Rng = new Random(1), ElapsedFraction = 0,
            Remaining = TimeSpan.FromMinutes(1), Name = (_, _) => { } };
        var effect = new ToastEffect();
        using var cts = new CancellationTokenSource();
        try
        {
            var apply = effect.ApplyAsync(ctx, null, cts.Token);
            Assert.Single(layer.Children); Assert.False(apply.IsCompleted);
            if (cancel) cts.Cancel();
            await apply.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Empty(layer.Children); Assert.False(effect.IsLive); Assert.Equal(0, host.Frames.Count);
        }
        finally { await effect.UndoAsync(TimeSpan.Zero); window.Close(); }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task EntranceWaitsForFramesAndCanBeDismissedWhileMoving(bool dismiss) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var below = new Border { Background = Brushes.Transparent };
        var layer = new Canvas { IsHitTestVisible = false };
        var window = new Window { Width = 600, Height = 400, Content = new Grid { Children = { below, layer } } };
        window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        var host = new Host(window, layer);
        var ctx = new PossessionContext { Host = host, Attribution = new Attribution(), Rung = PossessionRung.Drift,
            Intensity = PossessionIntensity.Gentle, Photosafe = false, Rng = new Random(1), ElapsedFraction = 0,
            Remaining = TimeSpan.FromMinutes(1), Name = (_, _) => { } };
        var effect = new ToastEffect();
        try
        {
            var apply = effect.ApplyAsync(ctx, null, CancellationToken.None);
            var toast = Assert.IsType<Border>(Assert.Single(layer.Children));
            host.Frames.Frame(TimeSpan.Zero); host.Frames.Frame(TimeSpan.FromMilliseconds(140));
            if (dismiss)
            {
                below.RaiseEvent(new PointerPressedEventArgs(below, new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true), layer,
                    new Point(Canvas.GetLeft(toast) + 5, Canvas.GetTop(toast) + 5), 0,
                    new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None));
                await Task.Delay(20);
                Assert.Single(layer.Children);
                host.Frames.Frame(TimeSpan.FromMilliseconds(200)); host.Frames.Frame(TimeSpan.FromMilliseconds(360));
                await apply.WaitAsync(TimeSpan.FromSeconds(2));
                Assert.Empty(layer.Children);
            }
            else
            {
                await Task.Delay(350);
                Assert.False(apply.IsCompleted);
                host.Frames.Frame(TimeSpan.FromMilliseconds(280));
                await apply.WaitAsync(TimeSpan.FromSeconds(2));
                Assert.Single(layer.Children);
            }
        }
        finally { await effect.UndoAsync(TimeSpan.Zero); window.Close(); }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ToastBorderAndFallbackGlyphUseEmber(bool border) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var layer = new Canvas { IsHitTestVisible = false };
        var window = new Window { Width = 600, Height = 400, Content = new Grid { Children = { layer } } };
        window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        var host = new Host(window, layer);
        var ctx = new PossessionContext { Host = host, Attribution = new Attribution(), Rung = PossessionRung.Drift,
            Intensity = PossessionIntensity.Gentle, Photosafe = false, Rng = new Random(1), ElapsedFraction = 0,
            Remaining = TimeSpan.FromMinutes(1), Name = (_, _) => { } };
        var effect = new ToastEffect();
        try
        {
            var apply = effect.ApplyAsync(ctx, null, CancellationToken.None);
            var toast = Assert.IsType<Border>(Assert.Single(layer.Children));
            var face = Assert.IsType<IconGlyph>(Assert.IsType<Grid>(toast.Child).Children[0]);
            Assert.Equal(IconKind.Diamond, face.Kind); Assert.Equal(IconVariant.Filled, face.Variant);
            Assert.Equal(15, face.Size); Assert.False(face.IsFallback);
            var brush = Assert.IsAssignableFrom<ISolidColorBrush>(border ? toast.BorderBrush : face.Foreground);
            Assert.Equal(Color.Parse("#FF8A5C"), brush.Color);
            await effect.UndoAsync(TimeSpan.Zero); await apply;
        }
        finally { await effect.UndoAsync(TimeSpan.Zero); window.Close(); }
    });

}
