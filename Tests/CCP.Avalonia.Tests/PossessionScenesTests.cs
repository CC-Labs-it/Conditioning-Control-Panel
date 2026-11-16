using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Possession.Scenes;
using ConditioningControlPanel.Services.Possession;
using Xunit;

namespace CCP.Avalonia.Tests;

public class PossessionScenesTests
{
    private sealed class Host : IPossessionHost
    {
        public IReadOnlyList<PossessionTarget> Targets => Array.Empty<PossessionTarget>();
        public bool IsUsable => true;
    }
    private sealed class Attribution(List<string> events) : IPossessionAttribution
    {
        public Task ChargeAsync(object target, CancellationToken ct, int durationMs = 400) { events.Add("charge"); return Task.CompletedTask; }
        public IDisposable Possess(object target) { events.Add("possess"); return new Release(events); }
        public void EdgePulse(double strength) { }
        public bool AnyPossessed => false;
        public void ReleaseAll() { }
        private sealed class Release(List<string> events) : IDisposable { public void Dispose() => events.Add("release"); }
    }
    private sealed class Scene(List<string> events) : PossessionSceneBase
    {
        public override string Id => "test";
        public readonly TaskCompletionSource Gate = new();
        public int Runs;
        protected override async Task RunCoreAsync(PossessionContext ctx, IPossessionHost host, Func<PossessionRole, PossessionTarget?> pick, CancellationToken ct)
        {
            Runs++;
            if (!await ChargeAsync(new TextBlock(), ct)) return;
            events.Add("act");
            OnUndo(() => events.Add("first"));
            OnUndo(() => events.Add("second"));
            await Gate.Task;
        }
    }
    [Fact]
    public void ChargePrecedesActAndUndoRestoresInReverseOnlyOnce() => AvaloniaTestDispatcher.Run(() =>
    {
        var events = new List<string>();
        var host = new Host();
        var ctx = new PossessionContext { Host = host, Attribution = new Attribution(events), Rung = PossessionRung.Melt,
            Intensity = PossessionIntensity.Gentle, Photosafe = false, Rng = new Random(1), ElapsedFraction = 0,
            Remaining = TimeSpan.Zero, Name = (_, _) => { } };
        var scene = new Scene(events);
        var run = scene.RunAsync(ctx, host, _ => null, CancellationToken.None);
        scene.RunAsync(ctx, host, _ => null, CancellationToken.None).GetAwaiter().GetResult();
        Assert.Equal(1, scene.Runs);
        scene.UndoAsync(TimeSpan.Zero).GetAwaiter().GetResult();
        scene.UndoAsync(TimeSpan.Zero).GetAwaiter().GetResult();
        Assert.Equal(new[] { "charge", "possess", "act", "second", "first", "release" }, events);
        scene.Gate.SetResult();
        run.GetAwaiter().GetResult();
    });
}
