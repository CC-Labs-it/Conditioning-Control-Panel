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
                ConditioningControlPanel.Services.Safety.PanicAftermath.EndQuiet();
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
    public Task DemoDispatchesEditedCannedCommentWithEntitlementWhileMasterIsOff() => Run((engine, s, lines) =>
    {
        s.KeywordTriggersEnabled = false;
        engine.Stop();
        CoreEntitlement.HasPremiumProvider = () => false;
        engine.FireDemo("sit", "Tutorial"); Assert.Empty(lines);
        CoreEntitlement.IsFreeTodayProvider = _ => true;
        engine.FireDemo(" ", "Tutorial"); Assert.Empty(lines);
        engine.FireDemo("sit", "Tutorial");
        Assert.Equal(("custom praise", false), Assert.Single(lines));
        Assert.Empty(s.KeywordTriggers);
        return Task.CompletedTask;
    });

    [Fact]
    public Task SingleTriggerDispatchesEveryEnabledCommentInOrder() => Run((engine, s, lines) =>
    {
        s.DisabledPhraseIds.Add("second:0"); // keep the second pool deterministic
        s.CustomCompanionPhrases.Add(new() { Id = "custom:second", Category = "second",
            Text = "second comment", Enabled = true });
        s.KeywordTriggers = new() { new() { Keyword = "sit", CooldownSeconds = 0, Actions = new() {
            new AvatarCommentAction { FallbackPhraseCategory = "PuppyPraise" },
            new AvatarCommentAction { Enabled = false, FallbackPhraseCategory = "PuppyPraise" },
            new AvatarCommentAction { FallbackPhraseCategory = "second" } } } };
        foreach (var c in "sit") engine.Character(c);
        Assert.Equal(new[] { ("custom praise", false), ("second comment", false) }, lines);
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

    [Fact]
    public Task RawPoolFallbackAndShutdownCancellation() => Run(async (_, s, lines) =>
    {
        s.CustomCompanionPhrases = new();
        s.DisabledPhraseIds = new() { "PuppyPraise:0" };
        await KeywordAvatarComment.DispatchAsync(new() { FallbackPhraseCategory = "PuppyPraise" }, new());
        Assert.Equal(("raw praise", false), Assert.Single(lines));
        lines.Clear();
        var pending = new TaskCompletionSource<string?>();
        HeadApp.Ai = new CommentAi { Reply = pending.Task };
        var dispatch = KeywordAvatarComment.DispatchAsync(new(), new());
        KeywordTriggerHead.Stop();
        pending.SetResult("late shutdown"); await dispatch;
        Assert.Empty(lines);
    });

    [Fact]
    public Task PostPanicQuietRefusesNewComments() => Run(async (_, _, lines) =>
    {
        ConditioningControlPanel.Services.Safety.PanicAftermath.ArmQuiet();
        try
        {
            await KeywordAvatarComment.DispatchAsync(new() { FallbackPhraseCategory = "PuppyPraise" }, new());
            Assert.Empty(lines);
        }
        finally { ConditioningControlPanel.Services.Safety.PanicAftermath.EndQuiet(); }
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
