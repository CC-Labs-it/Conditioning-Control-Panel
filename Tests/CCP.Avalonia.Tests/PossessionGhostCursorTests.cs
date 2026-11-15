using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Controls.Fx;
using ConditioningControlPanel.Avalonia.Possession;
using ConditioningControlPanel.Services.Possession;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Avalonia.Tests;

public class PossessionGhostCursorTests
{
    private sealed class Host(FakeFrames frames) : IAvaloniaPossessionHost
    {
        public IReadOnlyList<PossessionTarget> Targets { get; set; } = Array.Empty<PossessionTarget>();
        public bool IsUsable => true;
        public Window? PossessionWindow => null;
        public Canvas? GhostLayer { get; } = new();
        public IFrameSource PossessionFrames => frames;
        public PossessionRuntime PossessionEffectRuntime { get; } = new(frames);
        public Point PointerPosition => default;
        public PossessionPointer PossessionPointer { get; } = new();
        public Rect? BoundsOnGhostLayer(Control element) => new Rect(100, 100, 100, 40);
    }
    private sealed class Attribution : IPossessionAttribution
    {
        public Task ChargeAsync(object target, CancellationToken ct, int durationMs = 400) => Task.CompletedTask;
        public IDisposable Possess(object target) => new CancellationTokenSource();
        public void EdgePulse(double strength) { }
        public bool AnyPossessed => false;
        public void ReleaseAll() { }
    }

    private sealed class Command : ICommand
    {
        public int Calls;
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => Calls++;
    }

    [Theory]
    [InlineData(PossessionRung.Drift, false)]
    [InlineData(PossessionRung.Melt, true)]
    public void VisitNeverClicksAndUndoRestores(PossessionRung rung, bool press) => AvaloniaTestDispatcher.Run(() =>
    {
        var frames = new FakeFrames();
        var host = new Host(frames);
        host.GhostLayer!.Measure(new Size(400, 300)); host.GhostLayer.Arrange(new Rect(0, 0, 400, 300));
        var command = new Command();
        var toggle = new ToggleButton { Width = 100, Height = 40, Command = command };
        var window = new Window { Content = toggle }; window.Show();
        try
        {
            window.UpdateLayout();
            int clicks = 0; toggle.Click += (_, _) => clicks++;
            var target = new PossessionTarget { Visual = toggle, Role = PossessionRole.Toggle, Key = "toggle" };
            var context = new PossessionContext { Host = host, Attribution = new Attribution(), Rung = rung,
                Intensity = PossessionIntensity.Eerie, Photosafe = false, Rng = new Random(1), ElapsedFraction = 0,
                Remaining = TimeSpan.Zero, Name = (_, _) => { } };
            var effect = new GhostCursorEffect();
            Assert.Contains(PossessionAvaloniaHead.CreateEffects(), e => e is GhostCursorEffect);
            Assert.True(effect.CanApply(context, target));
            _ = effect.ApplyAsync(context, target, CancellationToken.None);
            var arrow = Assert.IsType<Path>(Assert.Single(host.GhostLayer.Children));
            frames.Frame(TimeSpan.Zero);
            frames.Frame(TimeSpan.FromMilliseconds(press ? 1500 : 1200));
            Assert.InRange(Canvas.GetLeft(arrow), 135, 165);
            Assert.InRange(Canvas.GetTop(arrow), 114, 126);
            frames.Frame(TimeSpan.FromMilliseconds(press ? 1990 : 1601));
            Assert.Equal(press, PossessionLease.IsLeased(toggle));
            if (press) { var lease = PossessionLease.Take(toggle)!; Assert.Equal(.96, lease.Scale.ScaleX, 2); lease.ReleaseImmediate(); }
            Assert.Equal(press ? 2 : 1, host.GhostLayer.Children.Count);
            Assert.Equal(0, clicks); Assert.Equal(0, command.Calls); Assert.False(toggle.IsChecked ?? false);
            effect.UndoAsync(TimeSpan.Zero).GetAwaiter().GetResult();
            Assert.False(PossessionLease.IsLeased(toggle));
            Assert.Empty(host.GhostLayer.Children); Assert.Equal(0, frames.Count);
            Assert.Equal(0, clicks); Assert.Equal(0, command.Calls); Assert.False(toggle.IsChecked ?? false);
        }
        finally { window.Close(); }
    });
    [Fact]
    public void DirectorPanicRemovesCursorAndLease() => AvaloniaTestDispatcher.Run(() =>
    {
        var frames = new FakeFrames();
        var host = new Host(frames);
        host.GhostLayer!.Measure(new Size(400, 300)); host.GhostLayer.Arrange(new Rect(0, 0, 400, 300));
        var toggle = new ToggleButton { Width = 100, Height = 40 };
        var window = new Window { Content = toggle }; window.Show();
        using var lockdown = new LockdownService();
        var effect = new GhostCursorEffect();
        var head = new PossessionDirectorHead { Settings = () => new AppSettings { LockdownPossessionEnabled = true,
            LockdownPossessionIntensity = (int)PossessionIntensity.Eerie }, Attribution = _ => new Attribution() };
        using var director = new PossessionDirector(lockdown, new[] { effect }, head);
        try
        {
            window.UpdateLayout();
            var target = new PossessionTarget { Visual = toggle, Role = PossessionRole.Toggle, Key = "toggle" };
            host.Targets = new[] { target };
            director.AttachHost(host); director.OnLockdownActivated(); director.ApplyRestartReset(PossessionRung.Melt);
            director.RequestReactive(effect.Id, target);
            frames.Frame(TimeSpan.Zero); frames.Frame(TimeSpan.FromMilliseconds(1901));
            Assert.True(PossessionLease.IsLeased(toggle));
            director.UndoAll();
            Assert.False(PossessionLease.IsLeased(toggle));
            Assert.Empty(host.GhostLayer.Children); Assert.Equal(0, frames.Count);
        }
        finally { director.UndoAll(); window.Close(); }
    });

    [Theory]
    [InlineData("en", "the ghost cursor")]
    [InlineData("de", "der Geisterzeiger")]
    [InlineData("es", "el cursor fantasma")]
    [InlineData("fr", "le curseur fantôme")]
    [InlineData("it", "il cursore fantasma")]
    [InlineData("ja", "幽霊カーソル")]
    [InlineData("ko", "유령 커서")]
    [InlineData("pt-BR", "o cursor fantasma")]
    [InlineData("ru", "призрачный курсор")]
    [InlineData("zh-CN", "幽灵光标")]
    public void NamesCursorInSelectedLanguage(string language, string expected) => AvaloniaTestDispatcher.Run(() =>
    {
        var loc = ConditioningControlPanel.Localization.LocalizationManager.Instance;
        var previous = loc.CurrentLanguage;
        var frames = new FakeFrames(); var host = new Host(frames);
        host.GhostLayer!.Measure(new Size(400, 300)); host.GhostLayer.Arrange(new Rect(0, 0, 400, 300));
        var button = new Button { Width = 100, Height = 40 };
        var window = new Window { Content = button }; window.Show();
        try
        {
            loc.SetLanguage(language); window.UpdateLayout();
            var target = new PossessionTarget { Visual = button, Role = PossessionRole.Button, Key = "button" };
            string? named = null;
            var context = new PossessionContext { Host = host, Attribution = new Attribution(), Rung = PossessionRung.Collapse,
                Intensity = PossessionIntensity.Eerie, Photosafe = false, Rng = new Random(1), ElapsedFraction = 0,
                Remaining = TimeSpan.Zero, Name = (_, name) => named = name };
            var effect = new GhostCursorEffect();
            _ = effect.ApplyAsync(context, target, CancellationToken.None);
            Assert.Equal(expected, named);
            effect.UndoAsync(TimeSpan.Zero).GetAwaiter().GetResult();
        }
        finally { window.Close(); loc.SetLanguage(previous); }
    });

    [Fact]
    public void ArrowHasEmberShadow() => AvaloniaTestDispatcher.Run(() =>
    {
        var frames = new FakeFrames(); var host = new Host(frames);
        host.GhostLayer!.Measure(new Size(400, 300)); host.GhostLayer.Arrange(new Rect(0, 0, 400, 300));
        var button = new Button { Width = 100, Height = 40 };
        var window = new Window { Content = button }; window.Show();
        try
        {
            window.UpdateLayout();
            var target = new PossessionTarget { Visual = button, Role = PossessionRole.Button, Key = "button" };
            var context = new PossessionContext { Host = host, Attribution = new Attribution(), Rung = PossessionRung.Melt,
                Intensity = PossessionIntensity.Eerie, Photosafe = false, Rng = new Random(1), ElapsedFraction = 0,
                Remaining = TimeSpan.Zero, Name = (_, _) => { } };
            var effect = new GhostCursorEffect(); _ = effect.ApplyAsync(context, target, CancellationToken.None);
            var arrow = Assert.IsType<Path>(Assert.Single(host.GhostLayer.Children));
            var shadow = Assert.IsType<DropShadowEffect>(arrow.Effect);
            Assert.Equal(EmberAttribution.Ember, shadow.Color); Assert.Equal(16, shadow.BlurRadius);
            Assert.Equal(0, shadow.OffsetX); Assert.Equal(0, shadow.OffsetY); Assert.Equal(.9, shadow.Opacity);
            effect.UndoAsync(TimeSpan.Zero).GetAwaiter().GetResult();
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 1.35)]
    public void PressEasesDownAndBack(bool photosafe, double slow) => AvaloniaTestDispatcher.Run(() =>
    {
        var frames = new FakeFrames(); var host = new Host(frames);
        host.GhostLayer!.Measure(new Size(400, 300)); host.GhostLayer.Arrange(new Rect(0, 0, 400, 300));
        var button = new Button { Width = 100, Height = 40 };
        var window = new Window { Content = button }; window.Show();
        try
        {
            window.UpdateLayout();
            var target = new PossessionTarget { Visual = button, Role = PossessionRole.Button, Key = "button" };
            var context = new PossessionContext { Host = host, Attribution = new Attribution(), Rung = PossessionRung.Melt,
                Intensity = PossessionIntensity.Eerie, Photosafe = photosafe, Rng = new Random(1), ElapsedFraction = 0,
                Remaining = TimeSpan.Zero, Name = (_, _) => { } };
            var effect = new GhostCursorEffect(); _ = effect.ApplyAsync(context, target, CancellationToken.None);
            frames.Frame(TimeSpan.Zero);
            frames.Frame(TimeSpan.FromMilliseconds(1900 * slow));
            var lease = PossessionLease.Take(button)!;
            Assert.Equal(1, lease.Scale.ScaleX, 5);
            frames.Frame(TimeSpan.FromMilliseconds((1900 + 45) * slow));
            Assert.Equal(.965, lease.Scale.ScaleX, 5); Assert.Equal(.965, lease.Scale.ScaleY, 5);
            frames.Frame(TimeSpan.FromMilliseconds((1900 + 90) * slow)); Assert.Equal(.96, lease.Scale.ScaleX, 5);
            frames.Frame(TimeSpan.FromMilliseconds((1900 + 120) * slow)); Assert.Equal(.96, lease.Scale.ScaleX, 5);
            frames.Frame(TimeSpan.FromMilliseconds((1900 + 215) * slow)); Assert.Equal(.995, lease.Scale.ScaleX, 5);
            frames.Frame(TimeSpan.FromMilliseconds((1900 + 310) * slow)); Assert.Equal(1, lease.Scale.ScaleX, 5);
            lease.ReleaseImmediate(); effect.UndoAsync(TimeSpan.Zero).GetAwaiter().GetResult();
            Assert.False(PossessionLease.IsLeased(button));
        }
        finally { window.Close(); }
    });

}
