using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Awareness;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class AwarenessObserverHeadTests
{
    private sealed class Foreground : IForegroundProbe
    {
        public ForegroundSample Read() => new(new IntPtr(1), "Spotify Premium", "spotify", false);
    }
    private sealed class Input : IInputProbe
    {
        public int IdleSeconds => 0;
        public bool IsTypingBurst => false;
        public void Start() { }
        public void Stop() { }
        public void Dispose() { }
    }
    private sealed class Mic : IMicrophoneProbe { public bool IsInUse(DateTime at) => false; }

    [Fact]
    public Task ConsentFrameWipeAndPanicFollowTheLiveObserver() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var oldSettings = CoreSettings.ServiceProvider;
        var oldPremium = CoreEntitlement.HasPremiumProvider;
        var oldFree = CoreEntitlement.IsFreeTodayProvider;
        var oldLifecycle = WindowAwarenessService.V2Lifecycle;
        var oldLedger = AwarenessLive.Ledger;
        var oldEntitlement = AwarenessObserver.EntitlementProvider;
        var oldPost = AwarenessObserver.PostToUi;
        var oldArbiter = AwarenessV2Routing.Arbiter;
        var oldBrain = BrainAwarenessLineSource.BrainProvider;
        var oldAi = BrainAwarenessLineSource.AiProvider;
        var oldBark = AvatarAwarenessSpeaker.RaiseBark;
        var oldDeliver = AvatarAwarenessSpeaker.DeliverLine;
        BrainAwarenessLineSource.BrainProvider = null;
        BrainAwarenessLineSource.AiProvider = null;
        AvatarAwarenessSpeaker.RaiseBark = null;
        AvatarAwarenessSpeaker.DeliverLine = null;
        var oldLive = (AwarenessLive.ObserverEnabled, AwarenessLive.Memory, AwarenessLive.ResetObserverState,
            AwarenessLive.ResetPacingState, AwarenessLive.ForgetPacingState);
        var flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
        var liveLock = typeof(AwarenessLive).GetField("Lock", flags)!.GetValue(null)!;
        var frameField = typeof(AwarenessLive).GetField("_lastFrame", flags)!;
        var timeField = typeof(AwarenessLive).GetField("_lastFrameAt", flags)!;
        object? oldFrame, oldFrameTime;
        lock (liveLock) { oldFrame = frameField.GetValue(null); oldFrameTime = timeField.GetValue(null); }
        var oldPause = AwarenessPause.PausedUntil;
        AwarenessPause.Resume();
        var dir = Directory.CreateTempSubdirectory("awareness-head-").FullName;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        CoreEntitlement.HasPremiumProvider = () => true;
        CoreEntitlement.IsFreeTodayProvider = _ => false;
        var s = settings.Current;
        s.UseAwarenessV2 = s.AwarenessModeEnabled = s.AwarenessConsentGiven = true;
        s.AwarenessConsentShownV2 = false;
        using var ledger = new ActivityLedger(Path.Combine(dir, "ledger.json"));
        try
        {
            AwarenessLive.Clear();
            AwarenessLive.Ledger = ledger;
            AvApp.InstallAwarenessObserver(ledger, new Foreground(), new Input(), new Mic());
            var vm = new AwarenessPrivacyViewModel(() => null);
            Assert.True(vm.CanReviewConsent);
            AvApp.WindowAwareness.Start();
            Assert.False(AwarenessLive.ObserverEnabled);
            vm.Sync();
            Assert.True(vm.IsLegacyPipeline);
            Assert.Null(AwarenessLive.LastFrame);
            AvApp.WindowAwareness.Stop();
            s.AwarenessConsentShownV2 = true;
            AvApp.WindowAwareness.Start();
            Assert.True(AwarenessLive.ObserverEnabled);
            Assert.True(AwarenessV2Routing.IsActive);
            var observer = Assert.IsType<AwarenessObserver>(AvApp.AwarenessObserverForTest);
            var now = DateTime.Now;
            await observer.TickAsync(now);
            await observer.TickAsync(now.AddSeconds(21));
            vm.Sync();
            Assert.NotNull(AwarenessLive.LastFrame);
            Assert.False(vm.IsLegacyPipeline);
            Assert.True(vm.IsWireLive);
            Assert.Contains("spotify", vm.WireJson, StringComparison.OrdinalIgnoreCase);
            var arbiter = Assert.IsType<ReactionArbiter>(AwarenessV2Routing.Arbiter);
            arbiter.Cooldowns.RecordDelivery(ReactionSource.Keyword, "forget-this-app", now.AddMinutes(-2));
            Assert.False(arbiter.Cooldowns.CanSpeak(ReactionSource.Keyword, "forget-this-app", now, out _));
            AwarenessLive.Forget("forget-this-app");
            Assert.True(arbiter.Cooldowns.CanSpeak(ReactionSource.Keyword, "forget-this-app", now, out _));
            Assert.NotNull(observer.CurrentAppId);
            Assert.NotEqual(0, arbiter.Cooldowns.LinesLastHour(now));
            AwarenessLive.WipeEverything();
            Assert.Null(AwarenessLive.LastFrame);
            Assert.Null(observer.CurrentAppId);
            Assert.Equal(0, arbiter.Cooldowns.LinesLastHour(now));
            Assert.Empty(ledger.KnownAppIds);
            await observer.TickAsync(now.AddSeconds(22));
            Assert.Null(AwarenessLive.LastFrame);
            var panic = PanicSurfaces.All.Single(surface => surface.Id == "awareness-companion");
            panic.Stop(null);
            Assert.False(observer.IsRunning);
            Assert.False(AwarenessLive.ObserverEnabled);
            Assert.Null(AwarenessV2Routing.Arbiter);
        }
        finally
        {
            AvApp.WindowAwareness.Stop();
            AvApp.ReleaseAwarenessObserver();
            AwarenessLive.Ledger = oldLedger;
            (AwarenessLive.ObserverEnabled, AwarenessLive.Memory, AwarenessLive.ResetObserverState,
                AwarenessLive.ResetPacingState, AwarenessLive.ForgetPacingState) = oldLive;
            lock (liveLock) { frameField.SetValue(null, oldFrame); timeField.SetValue(null, oldFrameTime); }
            AwarenessPause.Resume();
            if (oldPause is { } until) AwarenessPause.Pause(TimeSpan.FromTicks(1), until.AddTicks(-1));
            WindowAwarenessService.V2Lifecycle = oldLifecycle;
            AwarenessObserver.EntitlementProvider = oldEntitlement;
            AwarenessObserver.PostToUi = oldPost;
            AwarenessV2Routing.Attach(oldArbiter);
            BrainAwarenessLineSource.BrainProvider = oldBrain;
            BrainAwarenessLineSource.AiProvider = oldAi;
            AvatarAwarenessSpeaker.RaiseBark = oldBark;
            AvatarAwarenessSpeaker.DeliverLine = oldDeliver;
            CoreEntitlement.HasPremiumProvider = oldPremium;
            CoreEntitlement.IsFreeTodayProvider = oldFree;
            CoreSettings.ServiceProvider = oldSettings;
            Dispatcher.UIThread.RunJobs();
            try { Directory.Delete(dir, true); } catch { }
        }
    });
}
