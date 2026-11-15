using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Controls.Fx;
using ConditioningControlPanel.Avalonia.Possession;
using ConditioningControlPanel.Services.Possession;
using Xunit;

namespace CCP.Avalonia.Tests;

public class PossessionRetitleXpTests
{
    private sealed class Host(Window window) : IAvaloniaPossessionHost
    {
        public Window? PossessionWindow => window;
        public Canvas? GhostLayer => null;
        public IFrameSource PossessionFrames => throw new NotSupportedException();
        public PossessionRuntime PossessionEffectRuntime => throw new NotSupportedException();
        public Point PointerPosition => default;
        public PossessionPointer PossessionPointer { get; } = new();
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
    [InlineData("")]
    [InlineData("[SANDBOX] ")]
    public void RetitleRestoresBothShellTitles(string prefix) => AvaloniaTestDispatcher.Run(() =>
    {
        var text = new TextBlock { Name = "TxtTitleBarVersion", Text = "original bar" };
        var window = new Window { Title = prefix + "original window", Content = text };
        var scope = new NameScope();
        NameScope.SetNameScope(window, scope);
        scope.Register(text.Name!, text);
        var ctx = new PossessionContext
        {
            Host = new Host(window), Attribution = new Attribution(), Rung = PossessionRung.Collapse,
            Intensity = PossessionIntensity.FullDoki, Photosafe = false, Rng = new Random(1),
            ElapsedFraction = .6, Remaining = TimeSpan.FromMinutes(5), Name = (_, _) => { }
        };
        var effect = PossessionAvaloniaHead.CreateEffects().Single(e => e.Id == "retitle");
        Assert.True(effect.CanApply(ctx, null));
        effect.ApplyAsync(ctx, null, CancellationToken.None).GetAwaiter().GetResult();
        Assert.NotEqual(prefix + "original window", window.Title);
        Assert.Equal(prefix + text.Text, window.Title);
        effect.UndoAsync(TimeSpan.Zero).GetAwaiter().GetResult();
        Assert.Equal(prefix + "original window", window.Title);
        Assert.Equal("original bar", text.Text);
    });

    [Fact]
    public void RetitleIsEligibleOnlyAtCollapseAndFullDoki()
    {
        var e = PossessionAvaloniaHead.CreateEffects().Single(e => e.Id == "retitle");
        var meta = new PossessionEffectMeta(e.Id, e.MinRung, e.MinIntensity, e.UsesFlicker, e.Weight, e.Roles);
        Assert.False(PossessionDeck.EffectEligible(meta, PossessionRung.Melt, PossessionIntensity.FullDoki, false));
        Assert.False(PossessionDeck.EffectEligible(meta, PossessionRung.Collapse, PossessionIntensity.Eerie, false));
        Assert.True(PossessionDeck.EffectEligible(meta, PossessionRung.Collapse, PossessionIntensity.FullDoki, false));
        Assert.Equal(TimeSpan.Zero, e.HoldFor);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HeadingRetitleRestoresOnlyItsOwnText(bool newer) => AvaloniaTestDispatcher.Run(() =>
    {
        var text = new TextBlock { Text = "Original heading", Width = 200, Height = 30 };
        var window = new Window { Width = 300, Height = 100, Content = text };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var ctx = new PossessionContext
            {
                Host = new Host(window), Attribution = new Attribution(), Rung = PossessionRung.Collapse,
                Intensity = PossessionIntensity.FullDoki, Photosafe = false, Rng = new Random(1),
                ElapsedFraction = .6, Remaining = TimeSpan.FromMinutes(5), Name = (_, _) => { }
            };
            var effect = PossessionAvaloniaHead.CreateEffects().Single(e => e.Id == "retitleheading");
            var target = new PossessionTarget { Visual = text, Role = PossessionRole.Title, Key = "heading" };
            Assert.True(effect.CanApply(ctx, target));
            effect.ApplyAsync(ctx, target, CancellationToken.None).GetAwaiter().GetResult();
            Assert.NotEqual("Original heading", text.Text);
            if (newer) text.Text = "New heading";
            effect.UndoAsync(TimeSpan.Zero).GetAwaiter().GetResult();
            Assert.Equal(newer ? "New heading" : "Original heading", text.Text);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void HeadingRetitleRefusesBoundTextAndTimer() => AvaloniaTestDispatcher.Run(() =>
    {
        var text = new TextBlock { Text = "Heading", Width = 200, Height = 30 };
        var window = new Window { Width = 300, Height = 100, Content = text };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var ctx = new PossessionContext
            {
                Host = new Host(window), Attribution = new Attribution(), Rung = PossessionRung.Collapse,
                Intensity = PossessionIntensity.FullDoki, Photosafe = false, Rng = new Random(1),
                ElapsedFraction = .6, Remaining = TimeSpan.FromMinutes(5), Name = (_, _) => { }
            };
            var effect = new RetitleHeadingEffect();
            Assert.False(effect.CanApply(ctx, new PossessionTarget { Visual = text, Role = PossessionRole.Timer, Key = "timer" }));
            using var binding = text.Bind(TextBlock.TextProperty, new Binding("Text") { Source = new { Text = "Heading" } });
            Assert.False(effect.CanApply(ctx, new PossessionTarget { Visual = text, Role = PossessionRole.Title, Key = "heading" }));
        }
        finally { window.Close(); }
    });

}
