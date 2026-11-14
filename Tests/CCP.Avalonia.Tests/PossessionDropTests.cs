using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Data;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls.Fx;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Possession;
using ConditioningControlPanel.Services.Possession;
using Xunit;

namespace CCP.Avalonia.Tests;

public class PossessionDropTests
{
    private static PossessionTarget Target(Control control, PossessionRole role = PossessionRole.Title) =>
        new() { Visual = control, Role = role, Key = "title" };
    [Fact]
    public void RefusesBoundTextAndTimer() => AvaloniaTestDispatcher.Run(() =>
    {
        var text = new TextBlock { Text = "title" };
        Assert.Same(text, DropEffect.FindTarget(Target(text)));
        Assert.Null(DropEffect.FindTarget(Target(text, PossessionRole.Timer)));
        text.Bind(TextBlock.TextProperty, new Binding("Name") { Source = new { Name = "title" } });
        Assert.Null(DropEffect.FindTarget(Target(text)));
    });
    [Fact]
    public void RefusesTextThatCannotBeSafelyRewritten() => AvaloniaTestDispatcher.Run(() =>
    {
        var text = new TextBlock();
        foreach (var value in new[] { "", "abc", "a   ", "abc\ndef", "abc\rdef" })
        {
            text.Text = value;
            Assert.Null(DropEffect.FindTarget(Target(text)));
        }
        text.Text = "four";
        Assert.Same(text, DropEffect.FindTarget(Target(text, PossessionRole.Label)));
        text.Inlines = new InlineCollection { new Run("four"), new Run("more") };
        Assert.Null(DropEffect.FindTarget(Target(text)));
    });
    private sealed class Host(Window window, Canvas layer, FakeFrames frames, Canvas? floor = null) : IAvaloniaPossessionHost
    {
        public Window? PossessionWindow => window;
        public Canvas? GhostLayer => layer;
        public Canvas? RubbleFloor => floor;
        public Rect? FloorBoundsOverride { get; set; }
        public IFrameSource PossessionFrames => frames;
        public PossessionRuntime PossessionEffectRuntime { get; } = new(frames);
        public Point PointerPosition => default;
        public PossessionPointer PossessionPointer { get; } = new();
        public IReadOnlyList<PossessionTarget> Targets => Array.Empty<PossessionTarget>();
        public bool IsUsable => true;
        public Rect? BoundsOnGhostLayer(Control element) => element == floor && FloorBoundsOverride is { } bounds ? bounds : element.TranslatePoint(default, layer) is { } point
            ? new Rect(point, element.Bounds.Size) : null;
    }
    private sealed class Attribution : IPossessionAttribution
    {
        public Task ChargeAsync(object target, CancellationToken ct, int durationMs = 400) => Task.CompletedTask;
        public IDisposable Possess(object target) => new CancellationTokenSource();
        public void EdgePulse(double strength) { }
        public bool AnyPossessed => false;
        public void ReleaseAll() { }
    }
    private static PossessionContext Context(Host host) => new()
    {
        Host = host, Attribution = new Attribution(), Rung = PossessionRung.Collapse,
        Intensity = PossessionIntensity.Eerie, Photosafe = false, Rng = new Random(4),
        ElapsedFraction = 0.7, Remaining = TimeSpan.FromMinutes(1), Name = (_, _) => { }
    };
    private static (TextBlock text, Canvas layer, Window window, FakeFrames frames, Host host, DropEffect effect) Setup(string value, double width = 200)
    {
        var text = new TextBlock { Text = value, Width = width, Height = 30 }; var layer = new Canvas();
        var window = new Window { Width = 400, Height = 300, Content = new Grid { Children = { text, layer } } };
        window.Show(); Dispatcher.UIThread.RunJobs();
        var frames = new FakeFrames();
        return (text, layer, window, frames, new Host(window, layer, frames), new DropEffect());
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task CatalogDropFallsAndUndoRestoresExactText(bool panic) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        const string original = "Title é  Ω";
        var text = new TextBlock { Text = original, Width = 250, Height = 30, FontSize = 18,
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Top };
        var layer = new Canvas { IsHitTestVisible = false };
        var window = new Window { Width = 400, Height = 300, Content = new Grid { Children = { text, layer } } };
        window.Show(); Dispatcher.UIThread.RunJobs();
        var frames = new FakeFrames(); var host = new Host(window, layer, frames);
        var effect = Assert.IsType<DropEffect>(PossessionAvaloniaHead.CreateEffects().Single(e => e.Id == "drop"));
        var target = Target(text);
        var originalInlines = text.Inlines;
        try
        {
            Assert.True(effect.CanApply(Context(host), target));
            await effect.ApplyAsync(Context(host), target, CancellationToken.None);
            frames.Frame(TimeSpan.Zero); frames.Frame(TimeSpan.FromMilliseconds(5000));
            Assert.Single(layer.Children);
            var glyph = Assert.IsType<Image>(layer.Children[0]);
            var tr = Assert.IsType<TransformGroup>(glyph.RenderTransform).Children.OfType<TranslateTransform>().Single();
            frames.Frame(TimeSpan.FromMilliseconds(8000));
            Assert.True(tr.Y > 0);
            Assert.Contains(text.Inlines!, run => run is Run r && r.Foreground == Brushes.Transparent);
            Assert.Contains(text.Inlines!, run => run is Run r && r.Foreground != Brushes.Transparent && !r.IsSet(Run.ForegroundProperty));
            Assert.Equal(original, text.Text);
            double landed = tr.Y;
            if (panic)
            {
                PossessionRuntime.UndoAllLive();
                Assert.False(effect.IsLive);
            }
            else
            {
                var undo = effect.UndoAsync(TimeSpan.FromMilliseconds(600));
                frames.Frame(TimeSpan.FromMilliseconds(8100));
                frames.Frame(TimeSpan.FromMilliseconds(8400));
                Assert.InRange(tr.Y, 0.01, landed - 0.01);
                frames.Frame(TimeSpan.FromMilliseconds(8700));
                await undo;
            }
            Assert.Equal(original, text.Text);
            Assert.Same(originalInlines, text.Inlines);
            Assert.Empty(layer.Children);
            Assert.Equal(0, frames.Count);
            Assert.False(target.IsLive);
        }
        finally { PossessionRuntime.UndoAllLive(); window.Close(); }
    });
    [Fact]
    public Task CancellationAndExternalRewriteDoNotLeaveRubbleOrOverwriteNewText() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var (text, layer, window, frames, host, effect) = Setup("Before");
        using var cts = new CancellationTokenSource();
        try
        {
            await effect.ApplyAsync(Context(host), Target(text), cts.Token);
            frames.Frame(TimeSpan.Zero); frames.Frame(TimeSpan.FromMilliseconds(5000));
            Assert.Single(layer.Children);
            text.Text = "Changed";
            cts.Cancel(); Dispatcher.UIThread.RunJobs();
            Assert.Equal("Changed", text.Text);
            Assert.Empty(layer.Children);
            Assert.False(effect.IsLive);
        }
        finally { PossessionRuntime.UndoAllLive(); window.Close(); }
    });
    [Fact]
    public void CatalogEffectRefusesBoundTextAndTimer() => AvaloniaTestDispatcher.Run(() =>
    {
        var text = new TextBlock { Text = "Title", Width = 200, Height = 30 };
        var layer = new Canvas();
        var window = new Window { Width = 400, Height = 300, Content = new Grid { Children = { text, layer } } };
        window.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            var frames = new FakeFrames(); var host = new Host(window, layer, frames);
            var effect = PossessionAvaloniaHead.CreateEffects().Single(e => e.Id == "drop");
            Assert.True(effect.CanApply(Context(host), Target(text)));
            Assert.False(effect.CanApply(Context(host), Target(text, PossessionRole.Timer)));
            text.Bind(TextBlock.TextProperty, new Binding("Name") { Source = new { Name = "Title" } });
            Assert.False(effect.CanApply(Context(host), Target(text)));
        }
        finally { window.Close(); }
    });
    [Fact]
    public Task PanicDuringReturnStillEndsWithinTheReturnBound() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var (text, layer, window, frames, host, effect) = Setup("Returning");
        using var cts = new CancellationTokenSource();
        try
        {
            await effect.ApplyAsync(Context(host), Target(text), cts.Token);
            frames.Frame(TimeSpan.Zero); frames.Frame(TimeSpan.FromMilliseconds(5000));
            Assert.Single(layer.Children);
            var returning = effect.UndoAsync(TimeSpan.FromMilliseconds(600));
            PossessionRuntime.UndoAllLive();
            await returning.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Empty(layer.Children);
            Assert.Equal("Returning", text.Text);
            Assert.False(effect.IsLive);
        }
        finally { PossessionRuntime.UndoAllLive(); window.Close(); }
    });
    [Fact]
    public void OldCancellationCannotStopAReusedEffect() => AvaloniaTestDispatcher.Run(() =>
    {
        var (text, layer, window, frames, host, effect) = Setup("Returning");
        try
        {
            Assert.True(effect.ApplyAsync(Context(host), Target(text), CancellationToken.None).IsCompletedSuccessfully);
            Assert.True(effect.UndoAsync(TimeSpan.Zero).IsCompletedSuccessfully);
            Assert.True(effect.ApplyAsync(Context(host), Target(text), CancellationToken.None).IsCompletedSuccessfully);
            Assert.Equal(1, frames.Count);
            Dispatcher.UIThread.RunJobs();
            Assert.True(effect.IsLive);
            frames.Frame(TimeSpan.Zero); frames.Frame(TimeSpan.FromMilliseconds(5000));
            Assert.Single(layer.Children);
        }
        finally { PossessionRuntime.UndoAllLive(); window.Close(); }
    });
    [Fact]
    public Task FinishedPiecesParkTheFrameSourceAndReturnRestartsIt() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var (text, layer, window, frames, host, effect) = Setup("Drop");
        try
        {
            await effect.ApplyAsync(Context(host), Target(text), CancellationToken.None);
            frames.Frame(TimeSpan.Zero); frames.Frame(TimeSpan.FromMilliseconds(5000));
            Assert.Single(layer.Children);
            frames.Frame(TimeSpan.FromMilliseconds(8000));
            Assert.Equal(0, frames.Count);
            var undo = effect.UndoAsync(TimeSpan.FromMilliseconds(600));
            Assert.Equal(1, frames.Count);
            frames.Frame(TimeSpan.FromMilliseconds(8100)); frames.Frame(TimeSpan.FromMilliseconds(8700));
            await undo.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Empty(layer.Children);
            Assert.Equal(0, frames.Count);
        }
        finally { PossessionRuntime.UndoAllLive(); window.Close(); }
    });
    [Fact]
    public void DropStopsAtFortyPercentAndNeverDropsTheFirstCharacter() => AvaloniaTestDispatcher.Run(() =>
    {
        const string original = "ABCDEFGHIJKL";
        var (text, layer, window, frames, host, effect) = Setup(original, 250);
        try
        {
            Assert.True(effect.ApplyAsync(Context(host), Target(text), CancellationToken.None).IsCompletedSuccessfully);
            foreach (int ms in new[] { 0, 5000, 15000, 25000, 35000, 45000, 55000 })
                frames.Frame(TimeSpan.FromMilliseconds(ms));
            Assert.Equal(4, layer.Children.Count);
            Assert.StartsWith("A", Assert.IsType<Run>(text.Inlines![0]).Text);
            Assert.Equal(original, text.Text);
        }
        finally { PossessionRuntime.UndoAllLive(); window.Close(); }
    });
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public Task RubbleMovesToFloorAndIsRemovedOnUndo(int mode) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var text = new TextBlock { Text = "Drop", Width = 200, Height = 30 };
        var floor = new Canvas { Height = 28, VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Bottom };
        var layer = new Canvas();
        var window = new Window { Width = 400, Height = 300, Content = new Grid { Children = { text, floor, layer } } };
        window.Show(); Dispatcher.UIThread.RunJobs();
        var frames = new FakeFrames(); var effect = new DropEffect(); var host = new Host(window, layer, frames, floor);
        try
        {
            host.FloorBoundsOverride = new Rect(0, 100, 400, 0);
            await effect.ApplyAsync(Context(host), Target(text), CancellationToken.None);
            host.FloorBoundsOverride = new Rect(0, 200, 400, 0);
            frames.Frame(TimeSpan.Zero); frames.Frame(TimeSpan.FromMilliseconds(5000));
            var glyph = Assert.IsType<Image>(Assert.Single(layer.Children));
            var tr = Assert.IsType<TransformGroup>(glyph.RenderTransform).Children.OfType<TranslateTransform>().Single();
            double homeY = Canvas.GetTop(glyph); host.FloorBoundsOverride = new Rect(0, 250, 400, 0);
            frames.Frame(TimeSpan.FromMilliseconds(8000));
            Assert.Equal(200, homeY + tr.Y, 3);
            Assert.Empty(layer.Children); Assert.Same(glyph, Assert.Single(floor.Children));
            Assert.Equal(homeY, Canvas.GetTop(glyph) + 250, 3);
            if (mode == 1) PossessionRuntime.UndoAllLive();
            else
            {
                var undo = effect.UndoAsync(TimeSpan.FromMilliseconds(600));
                Assert.Empty(floor.Children); Assert.Single(layer.Children);
                if (mode == 0) { frames.Frame(TimeSpan.FromMilliseconds(8100)); frames.Frame(TimeSpan.FromMilliseconds(8700)); }
                await undo.WaitAsync(TimeSpan.FromSeconds(2));
            }
            Assert.Empty(floor.Children); Assert.Empty(layer.Children); Assert.Equal("Drop", text.Text);
        }
        finally { PossessionRuntime.UndoAllLive(); window.Close(); }
    });
}
