using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Possession;
using ConditioningControlPanel.Services.Possession;
using Xunit;

namespace CCP.Avalonia.Tests;

public class PossessionEffectBaseTests
{
    private sealed class Host : IPossessionHost
    {
        public IReadOnlyList<PossessionTarget> Targets => Array.Empty<PossessionTarget>();
        public bool IsUsable => true;
    }
    private sealed class Attribution : IPossessionAttribution
    {
        public readonly List<string> Events = new();
        public Task ChargeAsync(object target, CancellationToken ct, int durationMs = 400) { Events.Add("charge"); return Task.CompletedTask; }
        public IDisposable Possess(object target) { Events.Add("possess"); return new Release(Events); }
        public void EdgePulse(double strength) { }
        public bool AnyPossessed => false;
        public void ReleaseAll() { }
        private sealed class Release(List<string> events) : IDisposable { public void Dispose() => events.Add("release"); }
    }
    private sealed class Effect(bool big, bool fail) : PossessionEffectBase
    {
        public override string Id => "fake";
        public override PossessionRung MinRung => PossessionRung.Settle;
        public override double Weight => 1;
        public override TimeSpan HoldFor => TimeSpan.Zero;
        public override bool IsBig => big;
        public override IReadOnlyList<PossessionRole> Roles => new[] { PossessionRole.Label };
        protected override Task ApplyCoreAsync(PossessionContext ctx, PossessionTarget? target, CancellationToken ct)
        {
            var text = (TextBlock)target!.Visual;
            Override(text, TextBlock.TextProperty, "changed");
            Override(text, Visual.OpacityProperty, 0.2);
            Override(text, InputElement.IsHitTestVisibleProperty, false);
            if (fail) throw new InvalidOperationException();
            return Task.CompletedTask;
        }
        protected override Task UndoCoreAsync(TimeSpan duration) => Task.CompletedTask;
    }
    private static PossessionContext Context(Attribution attribution) => new()
    {
        Host = new Host(), Attribution = attribution, Rung = PossessionRung.Settle,
        Intensity = PossessionIntensity.Gentle, Photosafe = false, Rng = new Random(1),
        ElapsedFraction = 0, Remaining = TimeSpan.Zero,
        Name = (id, target) => attribution.Events.Add($"name:{id}:{target}")
    };
    private static PossessionTarget Target(TextBlock text) => new()
    { Visual = text, Role = PossessionRole.Label, Key = "label", DisplayName = "the label" };

    [Fact]
    public void ThrowingApplyRestoresEveryPropertyAndReleasesOutline() => AvaloniaTestDispatcher.Run(() =>
    {
        var text = new TextBlock { Text = "original", Opacity = 0.73, IsHitTestVisible = true };
        var target = Target(text);
        var attribution = new Attribution();
        var effect = new Effect(true, true);
        effect.ApplyAsync(Context(attribution), target, CancellationToken.None).GetAwaiter().GetResult();
        Assert.Equal("original", text.Text);
        Assert.Equal(0.73, text.Opacity);
        Assert.True(text.IsHitTestVisible);
        Assert.False(effect.IsLive);
        Assert.False(target.IsLive);
        Assert.Equal(new[] { "charge", "name:fake:the label", "possess", "release" }, attribution.Events);
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BigEffectsNameAfterChargeBeforeOutline(bool big) => AvaloniaTestDispatcher.Run(() =>
    {
        var text = new TextBlock { Text = "original" };
        var attribution = new Attribution();
        var effect = new Effect(big, false);
        effect.ApplyAsync(Context(attribution), Target(text), CancellationToken.None).GetAwaiter().GetResult();
        Assert.Equal(big ? new[] { "charge", "name:fake:the label", "possess" } : new[] { "charge", "possess" }, attribution.Events);
        Assert.Equal("changed", text.Text);
        effect.UndoAsync(TimeSpan.Zero).GetAwaiter().GetResult();
        Assert.Equal("original", text.Text);
        Assert.Equal("release", attribution.Events[^1]);
    });
}
