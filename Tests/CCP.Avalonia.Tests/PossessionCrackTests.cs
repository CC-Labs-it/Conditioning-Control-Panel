using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Possession;
using ConditioningControlPanel.Services.Possession;
using Xunit;

namespace CCP.Avalonia.Tests;

public class PossessionCrackTests
{
    private sealed class Host : IPossessionHost
    {
        public IReadOnlyList<PossessionTarget> Targets => Array.Empty<PossessionTarget>();
        public bool IsUsable => true;
    }
    private sealed class Attribution : IPossessionAttribution
    {
        public double Pulse;
        public Task ChargeAsync(object target, CancellationToken ct, int durationMs = 400) => Task.CompletedTask;
        public IDisposable Possess(object target) => throw new InvalidOperationException();
        public void EdgePulse(double strength) => Pulse = strength;
        public bool AnyPossessed => false;
        public void ReleaseAll() { }
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CrackPulsesAndOnlyShakesOutsidePhotosafe(bool photosafe) => AvaloniaTestDispatcher.Run(() =>
    {
        var attribution = new Attribution();
        var shakes = new List<(double, int)>();
        var effect = new CrackEffect((amount, ms) => shakes.Add((amount, ms)));
        var ctx = new PossessionContext { Host = new Host(), Attribution = attribution,
            Rung = PossessionRung.Collapse, Intensity = PossessionIntensity.Eerie, Photosafe = photosafe,
            Rng = new Random(1), ElapsedFraction = 0, Remaining = TimeSpan.Zero, Name = (_, _) => { } };
        Assert.True(effect.CanApply(ctx, null));
        effect.ApplyAsync(ctx, null, CancellationToken.None).GetAwaiter().GetResult();
        Assert.Equal(0.4, attribution.Pulse);
        if (photosafe) Assert.Empty(shakes);
        else Assert.Equal(new[] { (0.25, 180) }, shakes);
        effect.UndoAsync(TimeSpan.Zero).GetAwaiter().GetResult();
        Assert.False(effect.IsLive);
        Assert.Contains(PossessionAvaloniaHead.CreateEffects(), e => e.Id == "crack");
    });
}
