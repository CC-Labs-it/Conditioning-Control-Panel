using System;
using Avalonia.Threading;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Awareness;

namespace ConditioningControlPanel.Avalonia;

public partial class App
{
    private static AwarenessObserver? _awarenessObserver;
    internal static bool HasAwarenessObserver => _awarenessObserver != null;
    internal static AwarenessObserver? AwarenessObserverForTest => _awarenessObserver;

    // WPF App.xaml.cs:3136-3200: the observer, arbiter and privacy controls share one ledger and scorer.
    internal static void InstallAwarenessObserver(ActivityLedger ledger, IForegroundProbe? foreground = null, IInputProbe? input = null, IMicrophoneProbe? microphone = null, IMediaWatcher? media = null)
    {
        var memory = new StubCompanionMemory();
        var scorer = new WorthinessScorer();
        AwarenessObserver? observer = null;
        var arbiter = new ReactionArbiter(scorer: scorer,
            speaker: new AvatarAwarenessSpeaker(() => observer?.CurrentAppId),
            lineSource: new BrainAwarenessLineSource(), memory: memory);
        var state = new AwarenessAppState();
        try
        {
            observer = new AwarenessObserver(ledger, scorer, arbiter, memory, null,
                foreground ?? (OperatingSystem.IsWindows() ? new Platform.Win32AwarenessForegroundProbe() : new Platform.X11ForegroundProbe()),
                input ?? (OperatingSystem.IsWindows() ? new Platform.Win32AwarenessInputProbe() : new Platform.X11AwarenessInputProbe()),
                microphone ?? (OperatingSystem.IsWindows() ? new Platform.Win32AwarenessMicrophoneProbe() : new Platform.AwarenessMicrophoneProbe()),
                media ?? CreateAwarenessMediaWatcher(OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763), OperatingSystem.IsLinux(), SandboxNet.Active),
                state, null, attachAppState: state.Attach);
            _awarenessObserver = observer;
            AwarenessObserver.EntitlementProvider = () => WindowAwarenessService.HasEntitlement;
            AwarenessObserver.PostToUi = action => Dispatcher.UIThread.Post(action);
            AwarenessLive.Memory = memory;
            AwarenessLive.ResetObserverState = observer.ResetTransientState;
            AwarenessLive.ResetPacingState = () => { arbiter.Cooldowns.Reset(); scorer.Reset(); };
            AwarenessLive.ForgetPacingState = id => { arbiter.Cooldowns.Forget(id); scorer.Forget(id); };
            WindowAwarenessService.V2Lifecycle = start =>
            {
                if (start)
                {
                    observer.Start();
                    AwarenessLive.ObserverEnabled = observer.IsRunning;
                    if (observer.IsRunning) AwarenessV2Routing.Attach(arbiter);
                }
                else
                {
                    AwarenessV2Routing.Detach();
                    AwarenessLive.ObserverEnabled = false;
                    observer.Stop();
                    AwarenessLive.Clear();
                }
            };
        }
        catch
        {
            observer?.Dispose();
            state.Dispose();
            _awarenessObserver = null;
            AwarenessV2Routing.Detach();
            AwarenessLive.ObserverEnabled = false;
            AwarenessLive.Memory = null;
            AwarenessLive.ResetObserverState = null;
            AwarenessLive.ResetPacingState = null;
            AwarenessLive.ForgetPacingState = null;
            throw;
        }
    }

    // WPF App.xaml.cs:3150. Tests and sandbox profiles never read the real desktop's media session.
    internal static IMediaWatcher? CreateAwarenessMediaWatcher(bool windows, bool linux, bool sandboxed)
    {
        if (sandboxed) return null;
        if (windows) return new SmtcMediaWatcher();
        return linux ? new Platform.MprisMediaWatcher() : null;
    }

    internal static void ReleaseAwarenessObserver()
    {
        WindowAwarenessService.V2Lifecycle = null;
        AwarenessV2Routing.Detach();
        AwarenessLive.ObserverEnabled = false;
        _awarenessObserver?.Dispose();
        _awarenessObserver = null;
        AwarenessLive.Clear();
        AwarenessLive.Memory = null;
        AwarenessLive.ResetObserverState = null;
        AwarenessLive.ResetPacingState = null;
        AwarenessLive.ForgetPacingState = null;
        AwarenessObserver.EntitlementProvider = null;
        AwarenessObserver.PostToUi = null;
    }

    // WPF AwarenessProbes.cs:414-501: DtRH and Arcademy block ambient speech.
    internal sealed class AwarenessAppState : IAppStateProbe, IDisposable
    {
        private AchievementEngine? _achievements;
        private string? _recentId;
        private DateTime _recentAt;
        private readonly object _gate = new();
        internal void Attach()
        {
            if (_achievements != null || Achievements is not { } engine) return;
            _achievements = engine;
            engine.Unlocked += Unlocked;
        }
        private void Unlocked(object? sender, Achievement achievement)
        {
            lock (_gate) { _recentId = achievement.Id; _recentAt = DateTime.Now; }
        }
        public AppStateSample Read(DateTime at)
        {
            string? recent;
            lock (_gate) recent = (at - _recentAt).TotalMinutes <= 30 ? _recentId : null;
            var s = CoreSettings.Current;
            return new AppStateSample(CoreSession.IsSessionRunning, s.PlayerLevel, s.CurrentStreak, recent,
                CoreEngine.Video?.IsPlaying == true || Views.Windows.LockCardWindow.IsAnyOpen() || Views.Games.GameWindow.IsAwarenessBlockingGameOpen());
        }
        public void Dispose()
        {
            if (_achievements != null) _achievements.Unlocked -= Unlocked;
            _achievements = null;
        }
    }
}
