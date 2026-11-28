using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Awareness;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>The v2 observer host (WPF App.xaml.cs:3046-3099): the awareness switch starts it, its frames
/// reach the privacy card, the review action shows, and a panic stops the poll. Fake probes only.</summary>
public sealed class AwarenessObserverHostTests
{
    private sealed class Foreground : IForegroundProbe
    {
        public ForegroundSample? Read() => new(new IntPtr(1), "Notes draft", "testeditor", false);
    }

    private sealed class Input : IInputProbe
    {
        public int IdleSeconds => 0;
        public bool IsTypingBurst => false;
        public void Start() { }
        public void Stop() { }
        public void Dispose() { }
    }

    private sealed class Mic : IMicrophoneProbe
    {
        public bool IsInUse(DateTime at) => false;
    }

    [Fact]
    public Task SwitchStartsObserverCardShowsItsFrameAndPanicStopsIt() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var dir = Directory.CreateTempSubdirectory("ccp-awareness-observer-").FullName;
        var ledger = new ActivityLedger(Path.Combine(dir, "awareness_ledger.json"));
        var old = (AwarenessLive.Ledger, AwarenessLive.Memory, AwarenessLive.ResetObserverState,
            AwarenessLive.ResetPacingState, AwarenessLive.ForgetPacingState, AwarenessLive.ObserverEnabled);
        var oldHook = WindowAwarenessService.V2Lifecycle;
        var oldEntitlement = AwarenessObserver.EntitlementProvider;
        var oldPost = AwarenessObserver.PostToUi;
        var oldPause = AwarenessPause.PausedUntil;
        var flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
        var liveLock = typeof(AwarenessLive).GetField("Lock", flags)!.GetValue(null)!;
        var frameField = typeof(AwarenessLive).GetField("_lastFrame", flags)!;
        var timeField = typeof(AwarenessLive).GetField("_lastFrameAt", flags)!;
        object? oldFrame, oldAt;
        lock (liveLock) { oldFrame = frameField.GetValue(null); oldAt = timeField.GetValue(null); }
        using var scope = new TestSettingsScope();
        try
        {
            var s = CoreSettings.Current;
            s.UseAwarenessV2 = s.AwarenessModeEnabled = s.AwarenessConsentGiven = s.AwarenessConsentShownV2 = true;
            s.AwarenessIntensity = ConditioningControlPanel.Services.Awareness.AwarenessIntensity.Unhinged;
            CoreEntitlement.HasPremiumProvider = () => true;
            AwarenessObserver.EntitlementProvider = () => WindowAwarenessService.HasEntitlement;   // as startup
            AwarenessObserver.PostToUi = null;   // no poll timer: the test drives the ticks
            AwarenessPause.Resume();
            AwarenessLive.Clear();

            var observer = AvApp.CreateAwarenessObserver(ledger, new Foreground(), new Input(), new Mic());
            AvApp.InstallPrivacyLedger(ledger, observer);
            var vm = new AwarenessPrivacyViewModel(() => null);
            using (var awareness = new WindowAwarenessService(() => ""))
            {
                awareness.Start();   // the awareness switch: WPF WindowAwarenessService -> App.Awareness.Start
                Assert.True(observer.IsRunning);
                vm.Sync();
                Assert.False(vm.IsLegacyPipeline);
                Assert.True(vm.IsWireLive);
                Assert.True(vm.CanReviewConsent);

                var now = DateTime.Now;
                await observer.TickAsync(now);
                await observer.TickAsync(now.AddSeconds(AwarenessObserver.DwellGateSeconds + 1));
                Assert.Equal("testeditor", AwarenessLive.LastFrame?.AppId);
                Assert.Contains("testeditor", ledger.KnownAppIds);
                vm.Sync();
                Assert.Contains("Testeditor", vm.WireLine);
                Assert.True(vm.HasWireJson);

                // Panic: the v2 poll stops at once and the card tells the truth about it.
                PanicSurfaces.All.Single(x => x.Id == "awareness-observer").Stop(null);
                Assert.False(observer.IsRunning);
                vm.Sync();
                Assert.True(vm.IsLegacyPipeline);
                awareness.Stop();
            }
            AvApp.ReleasePrivacyLedger();
            Assert.Null(AvApp.AwarenessObserverHost);
            Assert.False(vm.CanReviewConsent);
        }
        finally
        {
            if (AvApp.AwarenessObserverHost != null) AvApp.ReleasePrivacyLedger();
            (AwarenessLive.Ledger, AwarenessLive.Memory, AwarenessLive.ResetObserverState,
                AwarenessLive.ResetPacingState, AwarenessLive.ForgetPacingState, AwarenessLive.ObserverEnabled) = old;
            lock (liveLock) { frameField.SetValue(null, oldFrame); timeField.SetValue(null, oldAt); }
            WindowAwarenessService.V2Lifecycle = oldHook;
            AwarenessObserver.EntitlementProvider = oldEntitlement;
            AwarenessObserver.PostToUi = oldPost;
            AwarenessPause.Resume();
            if (oldPause is { } until) AwarenessPause.Pause(TimeSpan.FromTicks(1), until.AddTicks(-1));
            ledger.Dispose();
            Directory.Delete(dir, recursive: true);
        }
    });
}
