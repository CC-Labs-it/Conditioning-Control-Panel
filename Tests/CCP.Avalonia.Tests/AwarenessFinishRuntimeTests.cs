using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.AIService;
using ConditioningControlPanel.Services.Moderation;
using Xunit;
using HeadApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class AwarenessFinishRuntimeTests
{
    // No font manager or windows: these exercise the real engine without opening the desktop.
    private static Task Run(Func<KeywordAudioRuntime, AppSettings, List<(string Text, bool Ai)>, Task> body) =>
        AvaloniaTestDispatcher.RunAsync(async () =>
        {
            var s = CoreSettings.Current;
            var saved = (s.KeywordTriggersEnabled, s.KeywordTriggerApps, s.KeywordTriggerAppScope,
                s.KeywordTriggers, s.KeywordGlobalCooldownSeconds, s.KeywordPerKeywordCooldownSeconds,
                s.CustomCompanionPhrases, s.DisabledPhraseIds, s.RemovedPhraseIds,
                CoreEntitlement.HasPremiumProvider, CoreEntitlement.IsFreeTodayProvider,
                CoreMods.PhrasesProvider, HeadApp.Ai, KeywordAvatarComment.Deliver);
            var lines = new List<(string, bool)>();
            var engine = new KeywordAudioRuntime();
            try
            {
                s.KeywordTriggersEnabled = true;
                s.KeywordTriggerAppScope = AwarenessAppScope.Everywhere;
                s.KeywordTriggers = new();
                s.KeywordGlobalCooldownSeconds = s.KeywordPerKeywordCooldownSeconds = 0;
                s.CustomCompanionPhrases = new() { new() { Id = "custom:test", Category = "PuppyPraise", Text = "custom praise", Enabled = true } };
                s.DisabledPhraseIds = new() { "PuppyPraise:0" };
                s.RemovedPhraseIds = new();
                CoreMods.PhrasesProvider = _ => new[] { "raw praise" };
                CoreEntitlement.HasPremiumProvider = () => true;
                CoreEntitlement.IsFreeTodayProvider = _ => false;
                HeadApp.Ai = null;
                KeywordAvatarComment.Deliver = (line, ai, _) => lines.Add((line, ai));
                engine.Start();
                await body(engine, s, lines);
            }
            finally
            {
                engine.Stop();
                KeywordAvatarComment.CancelPending();
                (s.KeywordTriggersEnabled, s.KeywordTriggerApps, s.KeywordTriggerAppScope,
                    s.KeywordTriggers, s.KeywordGlobalCooldownSeconds, s.KeywordPerKeywordCooldownSeconds,
                    s.CustomCompanionPhrases, s.DisabledPhraseIds, s.RemovedPhraseIds,
                    CoreEntitlement.HasPremiumProvider, CoreEntitlement.IsFreeTodayProvider,
                    CoreMods.PhrasesProvider, HeadApp.Ai, KeywordAvatarComment.Deliver) = saved;
            }
        });

    [Fact]
    public Task RecentAppsAreEightDistinctNewestFirstIncludingBlockedAppsButNeverOurOwn() => Run((engine, s, _) =>
    {
        s.KeywordTriggerAppScope = AwarenessAppScope.OnlyListed;
        s.KeywordTriggerApps = new(); // blocked apps must still be offered as ways out of an empty allow list
        engine.Character('a', "Editor");
        engine.Character('b', "browser");
        engine.Character('c', "EDITOR");
        engine.Character('d', "ccp", own: true);
        engine.Character('e');
        Assert.Equal(new[] { "Editor", "browser" }, engine.RecentForegroundApps);
        var snapshot = engine.RecentForegroundApps;
        for (var i = 0; i < 10; i++) engine.Character('x', "app" + i);
        Assert.Equal(Enumerable.Range(2, 8).Reverse().Select(i => "app" + i), engine.RecentForegroundApps);
        Assert.Equal(new[] { "Editor", "browser" }, snapshot);
        engine.Stop(); engine.RememberForeground("stopped", false);
        engine.Start(); s.KeywordTriggersEnabled = false; engine.RememberForeground("master-off", false);
        s.KeywordTriggersEnabled = true; CoreEntitlement.HasPremiumProvider = () => false;
        engine.RememberForeground("lapsed", false);
        Assert.DoesNotContain("lapsed", engine.RecentForegroundApps);
        Assert.DoesNotContain("master-off", engine.RecentForegroundApps);
        Assert.DoesNotContain("stopped", engine.RecentForegroundApps);
        return Task.CompletedTask;
    });

    [Fact]
    public Task SamplerUsesInjectedProcessSourceOnlyWhenArmedAndEntitled() => Run((_, s, _) =>
    {
        var source = KeywordTriggerHead.ForegroundSource;
        var platform = KeywordTriggerHead.PlatformForeground;
        var calls = 0;
        try
        {
            KeywordTriggerHead.ForegroundSource = () => { calls++; return ("sample-test", false); };
            KeywordTriggerHead.Stop(); KeywordTriggerHead.SampleForeground(); Assert.Equal(0, calls);
            KeywordTriggerHead.Sync(); KeywordTriggerHead.SampleForeground(); Assert.Equal(1, calls);
            Assert.Contains("sample-test", KeywordTriggerHead.Engine.RecentForegroundApps);
            CoreEntitlement.HasPremiumProvider = () => false;
            KeywordTriggerHead.SampleForeground(); Assert.Equal(1, calls);
            s.KeywordTriggersEnabled = false;
            KeywordTriggerHead.SampleForeground(); Assert.Equal(1, calls);
            // The sandbox (CCP_USERDATA_DIR, set by TestUserDataProfile) never reaches the real desktop read.
            var platformCalls = 0;
            KeywordTriggerHead.PlatformForeground = () => { platformCalls++; return ("real-desktop", false); };
            Assert.Equal((null, false), KeywordTriggerHead.ReadForeground());
            Assert.Equal(0, platformCalls);
        }
        finally { KeywordTriggerHead.Stop(); KeywordTriggerHead.ForegroundSource = source; KeywordTriggerHead.PlatformForeground = platform; }
        return Task.CompletedTask;
    });

    [Fact]
    public Task DemoDispatchesEditedCannedCommentAfterRecordingAndBypassesMasterAndCooldowns() => Run((engine, s, lines) =>
    {
        s.KeywordTriggersEnabled = false;
        engine.Stop();
        engine.Cooldowns.LastGlobal = DateTime.MaxValue;
        CoreEntitlement.HasPremiumProvider = () => false;
        engine.FireDemo("sit", "Tutorial"); Assert.Empty(lines); Assert.Empty(engine.Fires.Recent());
        CoreEntitlement.IsFreeTodayProvider = _ => true;
        engine.FireDemo(" ", "Tutorial"); Assert.Empty(lines);
        var recordedBeforeResponse = false;
        engine.TriggerFired += _ => recordedBeforeResponse = lines.Count == 0 && engine.Fires.Recent().Count == 1;
        engine.FireDemo("sit", "Tutorial");
        Assert.True(recordedBeforeResponse);
        Assert.Equal(("custom praise", false), Assert.Single(lines));
        Assert.Equal(DateTime.MaxValue, engine.Cooldowns.LastGlobal);
        Assert.Empty(s.KeywordTriggers);
        return Task.CompletedTask;
    });

    [Fact]
    public Task TypedMergedCommentsUseFirstEnabledActionOnce() => Run((engine, s, lines) =>
    {
        s.KeywordTriggers = new() {
            new() { Id = "preset:one", Keyword = "sit", CooldownSeconds = 0, Actions = new() { new AvatarCommentAction { FallbackPhraseCategory = "PuppyPraise" } } },
            new() { Id = "other", Keyword = "sit", CooldownSeconds = 0, Actions = new() { new AvatarCommentAction { FallbackPhraseCategory = "other" } } } };
        foreach (var c in "sit") engine.Character(c);
        Assert.Equal(("custom praise", false), Assert.Single(lines));
        return Task.CompletedTask;
    });

    [Fact]
    public Task AiSuccessEmptyFallbackAndPanicDuringRequestFollowWpf() => Run(async (_, _, lines) =>
    {
        var ai = new CommentAi();
        HeadApp.Ai = ai;
        var action = new AvatarCommentAction { FallbackPhraseCategory = "PuppyPraise", PromptTemplate = "test prompt" };
        var trigger = new KeywordTrigger { Keyword = "sit" };
        ai.Reply = Task.FromResult<string?>("AI praise");
        await KeywordAvatarComment.DispatchAsync(action, trigger);
        Assert.Equal(("AI praise", true), Assert.Single(lines));
        Assert.Equal(("sit", "test prompt"), ai.Request);
        lines.Clear(); ai.Reply = Task.FromResult<string?>(null);
        await KeywordAvatarComment.DispatchAsync(action, trigger);
        Assert.Equal(("custom praise", false), Assert.Single(lines));
        lines.Clear();
        var pending = new TaskCompletionSource<string?>(); ai.Reply = pending.Task;
        var dispatch = KeywordAvatarComment.DispatchAsync(action, trigger);
        Assert.False(dispatch.IsCompleted);
        Assert.Single(PanicSurfaces.All, x => x.Id == "keyword-comments").Stop(null);
        pending.SetResult("too late"); await dispatch;
        Assert.Empty(lines);
        ai.Reply = Task.FromException<string?>(new InvalidOperationException("test failure"));
        await KeywordAvatarComment.DispatchAsync(action, trigger);
        Assert.Empty(lines); // WPF catches exceptions without showing a fallback
    });

    private sealed class CommentAi : IAiService
    {
        public Task<string?> Reply = Task.FromResult<string?>(null);
        public (string, string?) Request;
        public bool IsAvailable => true;
        public int DailyRequestsRemaining => 10;
        public Task<string?> GetKeywordCommentAsync(string keyword, string? promptTemplate = null)
        { Request = (keyword, promptTemplate); return Reply; }
        public Task<AiReplyResult> SendAsync(IReadOnlyList<ChatMessage> messages, AiCallOptions options, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string> GetBambiReplyAsync(string userInput, bool isUserMessage = false) => throw new NotSupportedException();
        public Task<AiReplyResult> GetBambiReplyExAsync(string userInput, bool isUserMessage = false) => throw new NotSupportedException();
        public Task<string?> GetAwarenessReactionAsync(string detectedName, string category, string serviceName = "", string pageTitle = "", TimeSpan? duration = null) => throw new NotSupportedException();
        public Task<string?> GetStillOnReactionAsync(string displayName, string category, TimeSpan duration) => throw new NotSupportedException();
        public Task<string?> GetLockScreenReaction(string sentance, int mistakes, int amount, string? promptTemplate = null) => throw new NotSupportedException();
        public Task<string?> GetVideoDoneReaction(string title, string? promptTemplate = null) => throw new NotSupportedException();
        public void Dispose() { }
    }
}
