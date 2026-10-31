using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Deeper;
using ConditioningControlPanel.Models.Deeper;
using ConditioningControlPanel.Avalonia.Views.Deeper;
using System.Threading;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The persistent speak cue window (WPF Overlays/SpeakCueOverlay.cs): show, fade, hide, panic.</summary>
public sealed class SpeakCueOverlayTests
{
    private sealed class SteppedClock : TimeProvider
    {
        public long Now;
        public override long GetTimestamp() => Now;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public void Advance(int ms) => Now += TimeSpan.FromMilliseconds(ms).Ticks;
    }

    private static Task Run(Action<SteppedClock, List<double>> body) => Run((c, a) =>
    {
        body(c, a);
        return Task.CompletedTask;
    });

    private static async Task Run(Func<SteppedClock, List<double>, Task> body)
    {
        var (clock, prepare, alpha, warned) = (SpeakCueOverlay.Clock, SpeakCueOverlay.Prepare, SpeakCueOverlay.ApplyAlpha, SpeakCueOverlay.Warned);
        var c = new SteppedClock();
        var alphas = new List<double>();
        SpeakCueOverlay.Clock = c;
        SpeakCueOverlay.Prepare = (_, _) => true;
        SpeakCueOverlay.ApplyAlpha = (_, a) => alphas.Add(a);
        try
        {
            await AvaloniaTestDispatcher.RunAsync(async () =>
            {
                EnsureAvalonia();
                try { await body(c, alphas); }
                finally { SpeakCueOverlay.CloseActive(); }
            });
        }
        finally
        {
            (SpeakCueOverlay.Clock, SpeakCueOverlay.Prepare, SpeakCueOverlay.ApplyAlpha, SpeakCueOverlay.Warned) =
                (clock, prepare, alpha, warned);
        }
    }

    private static string[] Texts(SpeakCueOverlay w) =>
        w.GetLogicalDescendants().OfType<OutlinedTextBlock>().Select(t => t.Text).ToArray();

    [Fact]
    public Task ShowCue_DrawsEyebrowAndCue_FadesToPeak_HoldsUntilHidden() => Run((c, alphas) =>
    {
        SpeakCueOverlay.ShowCue("  ");
        Assert.Null(SpeakCueOverlay.Active);

        SpeakCueOverlay.ShowCue("Say YES");
        var w = SpeakCueOverlay.Active!;
        Assert.NotNull(w);
        Assert.True(w.IsVisible && w.Topmost && !w.ShowActivated && !w.ShowInTaskbar && !w.IsHitTestVisible);
        Assert.Equal(new[] { "SAY IT", "Say YES" }, Texts(w));
        c.Advance(90); w.Step();
        Assert.Equal(0.46, w.Alpha, 6);
        c.Advance(90); w.Step();
        Assert.Equal(SpeakCueState.PeakOpacity, w.Alpha);
        c.Advance(60_000); w.Step();                       // no auto-dismiss
        Assert.Same(w, SpeakCueOverlay.Active);

        SpeakCueOverlay.ShowCue("Say PLEASE");               // updates in place
        Assert.Same(w, SpeakCueOverlay.Active);
        Assert.Equal(new[] { "SAY IT", "Say PLEASE" }, Texts(w));

        SpeakCueOverlay.HideCue();
        c.Advance(179); w.Step();
        Assert.Same(w, SpeakCueOverlay.Active);              // still fading
        c.Advance(1); w.Step();
        Assert.Equal(0, alphas[^1]);
        Assert.Null(SpeakCueOverlay.Active);
        Assert.False(w.IsVisible);
    });

    [Fact]
    public Task ShowDuringFadeOut_KeepsTheWindow() => Run((c, _) =>
    {
        SpeakCueOverlay.ShowCue("Say YES");
        var w = SpeakCueOverlay.Active!;
        c.Advance(200); w.Step();
        SpeakCueOverlay.HideCue();
        c.Advance(100); w.Step();
        SpeakCueOverlay.ShowCue("Say YES");
        c.Advance(500); w.Step();
        Assert.Same(w, SpeakCueOverlay.Active);
        Assert.Equal(SpeakCueState.PeakOpacity, w.Alpha);
    });

    [Fact]
    public Task RefusedPlatform_ShowsNothing() => Run((_, _) =>
    {
        SpeakCueOverlay.Prepare = (_, _) => false;
        SpeakCueOverlay.Warned = false;
        SpeakCueOverlay.ShowCue("Say YES");
        Assert.Null(SpeakCueOverlay.Active);
        Assert.True(SpeakCueOverlay.Warned);              // warned once; Run restores it
    });

    [Fact]
    public Task Panic_ClosesTheCueAtOnce() => Run((_, _) =>
    {
        SpeakCueOverlay.ShowCue("Say YES");
        var w = SpeakCueOverlay.Active!;
        PanicSurfaces.All.Single(s => s.Id == "speak-cue").Stop(null);
        Assert.Null(SpeakCueOverlay.Active);
        Assert.False(w.IsVisible);
    });

    private static SpeakPromptPlan Plan(SpeakCueMode mode, int interval = 250) => new(
        new TriggerEffectAction { SpeakTarget = "YES", SpeakCueMode = mode, SpeakCueIntervalMs = interval },
        DateTime.UtcNow);

    [Fact]
    public Task PromptPersistent_StartsOnce_StopsWithFade_AndCannotRestart() => Run((clock, _) =>
    {
        var flashes = new List<string>();
        using var cue = new SpeakPromptCueSession(Plan(SpeakCueMode.Persistent), flashes.Add);
        cue.Start();
        var window = SpeakCueOverlay.Active;
        Assert.NotNull(window);
        Assert.Equal("Say YES", window.CueText);
        cue.Start();
        cue.Pulse();
        Assert.Same(window, SpeakCueOverlay.Active);
        Assert.Empty(flashes);
        Assert.Null(cue.CueInterval);
        clock.Advance(180); window.Step();
        cue.Dispose();
        clock.Advance(179); window.Step();
        Assert.Same(window, SpeakCueOverlay.Active);
        clock.Advance(1); window.Step();
        Assert.Null(SpeakCueOverlay.Active);
        cue.Start();
        Assert.Null(SpeakCueOverlay.Active);
    });

    [Fact]
    public Task PromptIntermittent_FlashesImmediatelyAndOnPulse_StopsTimerOnCancellation() => Run((_, _) =>
    {
        var flashes = new List<string>();
        using var cancel = new CancellationTokenSource();
        using var cue = new SpeakPromptCueSession(Plan(SpeakCueMode.Intermittent, 12), flashes.Add, cancel.Token);
        cue.Start();
        Assert.Equal(new[] { "Say YES" }, flashes);
        Assert.Equal(TimeSpan.FromMilliseconds(80), cue.CueInterval);
        cue.Pulse();
        Assert.Equal(new[] { "Say YES", "Say YES" }, flashes);
        Assert.Null(SpeakCueOverlay.Active);
        cancel.Cancel();
        Assert.Null(cue.CueInterval);
        cue.Pulse();
        cue.Start();
        Assert.Equal(2, flashes.Count);
    });

    [Fact]
    public Task PromptStoppedOrCancelledBeforeStart_ShowsNothing() => Run((_, _) =>
    {
        using var stopped = new SpeakPromptCueSession(Plan(SpeakCueMode.Persistent), _ => Assert.Fail());
        stopped.Dispose();
        stopped.Start();
        using var cancelled = new SpeakPromptCueSession(Plan(SpeakCueMode.Persistent), _ => Assert.Fail(),
            new CancellationToken(true));
        cancelled.Start();
        Assert.Null(SpeakCueOverlay.Active);
    });

    [Fact]
    public Task PromptFlashFailure_DoesNotAbortCueOrCleanup() => Run((_, _) =>
    {
        var attempts = 0;
        using var cue = new SpeakPromptCueSession(Plan(SpeakCueMode.Intermittent), _ =>
        {
            attempts++;
            throw new InvalidOperationException("flash unavailable");
        });
        cue.Start();
        Assert.NotNull(cue.CueInterval);
        cue.Pulse();
        Assert.Equal(2, attempts);
        cue.Dispose();
        Assert.Null(cue.CueInterval);
    });

    [Fact]
    public Task PromptTimer_DeliversSubsequentCue() => Run(async (_, _) =>
    {
        var repeated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var flashes = 0;
        using var cue = new SpeakPromptCueSession(Plan(SpeakCueMode.Intermittent, 80), _ =>
        {
            if (++flashes == 2) repeated.TrySetResult();
        });
        cue.Start();
        Assert.Same(repeated.Task, await Task.WhenAny(repeated.Task, Task.Delay(2000)));
        cue.Dispose();
        var stoppedAt = flashes;
        await Task.Delay(160);
        Assert.Equal(stoppedAt, flashes);
    });

    private static void EnsureAvalonia()
    {
        Assert.True(AvaloniaTestDispatcher.IsDispatcherThread);
        if (Application.Current is null)
        {
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        }
    }
}
