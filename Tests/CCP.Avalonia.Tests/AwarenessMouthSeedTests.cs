using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Awareness;
using ConditioningControlPanel.Services.Bark;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>BarkHead seeds the Core awareness mouth the way WPF App.xaml.cs:3086 and the App.* reads of
/// AwarenessRouting.cs did: lines reach the tube (WPF Reactions.cs:287), barks and the shared floor reach the
/// bark engine, the echo reaches the keyword engine, and v2 owns reactions only with an arbiter attached.</summary>
public sealed class AwarenessMouthSeedTests
{
    [Fact]
    public Task SeededMouthSpeaksThroughTheTubeAndTheBarkEngine() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var oldSettings = CoreSettings.ServiceProvider;
        var service = new SettingsService();   // awareness reads the service itself, never the fallback
        CoreSettings.ServiceProvider = () => service;
        var s = service.Current;
        var seams = (AvatarAwarenessSpeaker.RaiseBark, AvatarAwarenessSpeaker.DeliverLine, AvatarAwarenessSpeaker.CompanionBusy,
            AvatarAwarenessSpeaker.LineSpoken, AvatarAwarenessSpeaker.MuteKeywordEcho, BrainAwarenessLineSource.BrainProvider,
            BrainAwarenessLineSource.AiProvider, WindowAwarenessService.V2OwnsReactionsProvider);
        var (oldEntitlement, oldTube, oldEcho) = (AwarenessObserver.EntitlementProvider, BarkHead.TubeProvider, BarkHead.EchoTarget);
        var tube = new AvatarTubeWindow(null);
        tube.SpeechAudioPlayer = (_, _, _, done) => done?.Invoke();
        try
        {
            (s.UseAwarenessV2, s.AwarenessModeEnabled, s.AwarenessConsentGiven, s.AwarenessConsentShownV2) = (true, true, true, true);
            (s.AvatarMuted, s.BarkChatSuppressionMs) = (false, 10000);
            AwarenessObserver.EntitlementProvider = () => true;
            tube.Show();
            AvApp.WindowAwareness.Stop();
            BarkHead.TubeProvider = () => tube;
            var echoes = 0;
            BarkHead.EchoTarget = () => { echoes++; return KeywordTriggerHead.Engine; };
            var bark = new BarkEngine(() => BarkRuleSet.Empty);
            BarkHead.SeedAwarenessMouth(bark);

            Assert.Same(bark, AvatarAwarenessSpeaker.RaiseBark!.Target);
            Assert.Equal(nameof(BarkEngine.RaiseAwarenessBark), AvatarAwarenessSpeaker.RaiseBark.Method.Name);
            Assert.Same(bark, AvatarAwarenessSpeaker.LineSpoken!.Target);
            Assert.Equal(nameof(BarkEngine.NotifyExternalLineSpoken), AvatarAwarenessSpeaker.LineSpoken.Method.Name);
            Assert.Same(AvApp.Brain, BrainAwarenessLineSource.BrainProvider!());
            Assert.Same(AvApp.Ai, BrainAwarenessLineSource.AiProvider!());

            var speaker = new AvatarAwarenessSpeaker(() => null);
            Assert.True(speaker.TrySpeakLine("you again", RarityTier.Rare));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("you again", tube.FindControl<TextBlock>("TxtSpeech")!.Text);
            Assert.Equal(1, echoes);
            // Her AI bubble is up, so the tube now reads busy and the next quip defers (WPF IsCompanionBusy).
            Assert.False(speaker.TrySpeakLine("and again", RarityTier.Common));
            BarkHead.TubeProvider = () => null;
            s.BarkChatSuppressionMs = 0;
            Assert.False(speaker.TrySpeakLine("no tube", RarityTier.Common));
            Assert.Equal(1, echoes);

            Assert.False(WindowAwarenessService.V2OwnsReactionsProvider!());
            AwarenessV2Routing.Attach(new NullArbiter());
            try { Assert.True(WindowAwarenessService.V2OwnsReactionsProvider()); }
            finally { AwarenessV2Routing.Detach(); }
        }
        finally
        {
            tube.Close();
            CoreSettings.ServiceProvider = oldSettings;
            (AvatarAwarenessSpeaker.RaiseBark, AvatarAwarenessSpeaker.DeliverLine, AvatarAwarenessSpeaker.CompanionBusy,
                AvatarAwarenessSpeaker.LineSpoken, AvatarAwarenessSpeaker.MuteKeywordEcho, BrainAwarenessLineSource.BrainProvider,
                BrainAwarenessLineSource.AiProvider, WindowAwarenessService.V2OwnsReactionsProvider) = seams;
            (AwarenessObserver.EntitlementProvider, BarkHead.TubeProvider, BarkHead.EchoTarget) = (oldEntitlement, oldTube, oldEcho);
        }
        return Task.CompletedTask;
    });

    private sealed class NullArbiter : IReactionArbiter
    {
        public Task<ArbiterDecision> SubmitAsync(ContextFrame frame, CancellationToken cancellationToken = default) =>
            Task.FromResult(ArbiterDecision.Silent("test"));
        public void RecordExternalLine(ReactionSource source, string? appId = null) { }
        public bool CanSpeak(ReactionSource source, string? appId = null) => true;
    }
}
