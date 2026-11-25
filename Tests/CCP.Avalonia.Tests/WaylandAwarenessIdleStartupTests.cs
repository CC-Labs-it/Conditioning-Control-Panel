using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Awareness;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class WaylandAwarenessIdleStartupTests
{
    private sealed class Input : IInputProbe
    {
        public int IdleSeconds => 0;
        public bool IsTypingBurst => false;
        public void Start() { }
        public void Stop() { }
        public void Dispose() { }
    }
    private sealed class Foreground : IForegroundProbe
    {
        public ForegroundSample Read() => new(new IntPtr(1), "Spotify Premium", "spotify", false);
    }
    private sealed class Microphone : IMicrophoneProbe { public bool IsInUse(DateTime at) => false; }
    private sealed class AppState : IAppStateProbe { public AppStateSample Read(DateTime at) => AppStateSample.Empty; }

    private static async Task Until(Func<bool> condition)
    {
        for (int i = 0; i < 200 && !condition(); i++) await Task.Delay(10);
        Assert.True(condition());
    }

    [Fact]
    public async Task ExistingIdleStaysUnknownAndDoesNotAccrueUntilUserResumes()
    {
        var oldSettings = CoreSettings.ServiceProvider;
        var oldEntitlement = AwarenessObserver.EntitlementProvider;
        var oldPost = AwarenessObserver.PostToUi;
        var oldPause = AwarenessPause.PausedUntil;
        AwarenessPause.Resume();
        var directory = Directory.CreateTempSubdirectory("awareness-wayland-").FullName;
        var service = new SettingsService();
        var settings = service.Current;
        settings.UseAwarenessV2 = settings.AwarenessModeEnabled = settings.AwarenessConsentGiven = settings.AwarenessConsentShownV2 = true;
        CoreSettings.ServiceProvider = () => service;
        AwarenessObserver.EntitlementProvider = () => true;
        AwarenessObserver.PostToUi = null;
        long milliseconds = 0;
        var clock = DateTime.Today.AddHours(12);
        var notify = new WaylandIdleNotify(30_000) { Now = () => milliseconds };
        static Task<int?> Fallback() => Task.FromResult<int?>(400);
        Assert.Null(await WaylandAwarenessInputProbe.ReadCompositor(notify, Fallback));
        notify.Live = true;
        using var ledger = new ActivityLedger(Path.Combine(directory, "ledger.json"), () => clock, () => 30);
        var input = new WaylandAwarenessInputProbe(new Input(), () => WaylandAwarenessInputProbe.ReadCompositor(notify, Fallback));
        var policy = new AwarenessPolicySettings(Array.Empty<string>(), Array.Empty<string>(), true, true);
        using var observer = new AwarenessObserver(ledger, new WorthinessScorer(), new ReactionArbiter(), new StubCompanionMemory(),
            () => clock, new Foreground(), input, new Microphone(), null, new AppState(), () => policy);
        try
        {
            observer.Start();
            // The compositor starts its own timer now, although the user was already away.
            Assert.Equal(AwarenessObserver.AfkSuspendSeconds, input.IdleSeconds);
            milliseconds = 30_000;
            notify.OnEvent(0);
            clock = clock.AddSeconds(30);
            await observer.TickAsync(clock);
            milliseconds = 179_000;
            clock = clock.AddSeconds(149);
            Assert.Null(WaylandAwarenessInputProbe.ConfirmedIdle(notify));
            await observer.TickAsync(clock);
            Assert.Empty(ledger.KnownAppIds);

            notify.OnEvent(1); // a real resume proves that fresh input happened
            await Until(() => input.IdleSeconds == 0);
            await observer.TickAsync(clock);
            Assert.Contains("spotify", ledger.KnownAppIds);
            milliseconds += 30_000;
            notify.OnEvent(0);
            milliseconds += 10_000;
            Assert.Equal(40, WaylandAwarenessInputProbe.ConfirmedIdle(notify));
            notify.Live = false;
            notify.Done = true;
            Assert.Equal(400, await WaylandAwarenessInputProbe.ReadCompositor(notify, Fallback));
        }
        finally
        {
            observer.Dispose();
            CoreSettings.ServiceProvider = oldSettings;
            AwarenessObserver.EntitlementProvider = oldEntitlement;
            AwarenessObserver.PostToUi = oldPost;
            AwarenessPause.Resume();
            if (oldPause is { } until) AwarenessPause.Pause(TimeSpan.FromTicks(1), until.AddTicks(-1));
            try { Directory.Delete(directory, true); } catch { }
        }
    }

    [Fact]
    public async Task RestartDiscardsAnOldPendingIdleRead()
    {
        var first = new TaskCompletionSource<int?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource<int?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        Task<int?> Read()
        {
            if (Interlocked.Increment(ref calls) == 1) { firstStarted.TrySetResult(); return first.Task; }
            secondStarted.TrySetResult(); return second.Task;
        }
        using var probe = new WaylandAwarenessInputProbe(new Input(), Read);
        probe.Start();
        Assert.Equal(AwarenessObserver.AfkSuspendSeconds, probe.IdleSeconds);
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var oldCompletion = Assert.IsAssignableFrom<Task>(probe.PendingRead);
        probe.Stop();
        probe.Start();
        Assert.Equal(AwarenessObserver.AfkSuspendSeconds, probe.IdleSeconds);
        await secondStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        first.SetResult(0);
        // The old completion has run while the new read remains pending.
        await oldCompletion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(AwarenessObserver.AfkSuspendSeconds, probe.IdleSeconds);
        second.SetResult(400);
        await Until(() => probe.IdleSeconds == 400);
    }
}
