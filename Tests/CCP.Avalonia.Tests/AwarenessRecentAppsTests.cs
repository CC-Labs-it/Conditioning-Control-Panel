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
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using ConditioningControlPanel.Avalonia.Views.Tabs;
namespace CCP.Avalonia.Tests;
public sealed class AwarenessFinishRuntimeTests
{
    private static Task Run(Func<KeywordAudioRuntime, AppSettings, List<(string Text, bool Ai)>, Task> body) => // No font manager or windows: these exercise the real engine without opening the desktop.
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
        public Task<AiReplyResult> SendAsync(IReadOnlyList<ConditioningControlPanel.Services.AIService.ChatMessage> messages, AiCallOptions options, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string> GetBambiReplyAsync(string userInput, bool isUserMessage = false) => throw new NotSupportedException();
        public Task<AiReplyResult> GetBambiReplyExAsync(string userInput, bool isUserMessage = false) => throw new NotSupportedException();
        public Task<string?> GetAwarenessReactionAsync(string detectedName, string category, string serviceName = "", string pageTitle = "", TimeSpan? duration = null) => throw new NotSupportedException();
        public Task<string?> GetStillOnReactionAsync(string displayName, string category, TimeSpan duration) => throw new NotSupportedException();
        public Task<string?> GetLockScreenReaction(string sentance, int mistakes, int amount, string? promptTemplate = null) => throw new NotSupportedException();
        public Task<string?> GetVideoDoneReaction(string title, string? promptTemplate = null) => throw new NotSupportedException();
        public void Dispose() { }
    }
}
public sealed class AwarenessFinishViewTests
{
    private static void Setup()
    {
        if (Application.Current == null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }
    [Fact]
    public void CommentsWaitForBubbleAndVoiceBoundRetriesAndNeverSpeakAfterPanic() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        var saved = (CoreAudio.PlayStoppableProvider, KeywordAvatarComment.ScheduleRetry, KeywordAvatarComment.TubeSource);
        var pending = new Queue<Action>();
        Action? finished = null;
        var file = Path.GetTempFileName();
        AvatarTubeWindow? tube = null;
        var volume = CoreSettings.Current.MasterVolume;
        try
        {
            ConditioningControlPanel.Services.Safety.PanicAftermath.EndQuiet();
            CoreSettings.Current.MasterVolume = 100;
            CoreAudio.PlayStoppableProvider = (_, _, _, _, done) => { finished = done; return () => { }; };
            KeywordAvatarComment.ScheduleRetry = (delay, fire) =>
            { Assert.Equal(TimeSpan.FromMilliseconds(750), delay); pending.Enqueue(fire); };
            tube = new AvatarTubeWindow(null); tube.Show(); Dispatcher.UIThread.RunJobs();
            KeywordAvatarComment.TubeSource = () => tube; // SetupWithoutStarting has no desktop lifetime/window list: provide its shown tube.
            tube.GigglePriority("original", playSound: false, aiGenerated: false, phraseAudioPath: file);
            tube.EndSpeechBubble();
            Assert.False(tube.IsSpeaking); Assert.False(tube.SpokenAudioDone.IsCompleted);
            var s = CoreSettings.Current; // A real demo routes through the default delivery, not a captured test callback.
            var entitlement = CoreEntitlement.HasPremiumProvider;
            var phrases = s.CustomCompanionPhrases;
            var ai = ConditioningControlPanel.Avalonia.App.Ai;
            try
            {
                CoreEntitlement.HasPremiumProvider = () => true;
                ConditioningControlPanel.Avalonia.App.Ai = null;
                s.CustomCompanionPhrases = new() { new() { Id = "retry:test", Category = "retry-test", Text = "comment", Enabled = true } };
                KeywordAvatarComment.DispatchAsync(new AvatarCommentAction { FallbackPhraseCategory = "retry-test" }, new()).GetAwaiter().GetResult();
                Dispatcher.UIThread.RunJobs(); Assert.Single(pending);
                finished!(); Dispatcher.UIThread.RunJobs(); Assert.True(tube.SpokenAudioDone.IsCompleted);
                pending.Dequeue()(); Assert.Equal("comment", tube.FindControl<TextBlock>("TxtSpeech")!.Text);
                KeywordAvatarComment.DispatchAsync(new AvatarCommentAction { FallbackPhraseCategory = "retry-test" }, new()).GetAwaiter().GetResult();
                Dispatcher.UIThread.RunJobs();
                for (var i = 0; i < 16; i++) pending.Dequeue()();
                Assert.Empty(pending); // ~12 seconds of nonstop speech drops the comment
                KeywordAvatarComment.DispatchAsync(new AvatarCommentAction { FallbackPhraseCategory = "retry-test" }, new()).GetAwaiter().GetResult();
                Dispatcher.UIThread.RunJobs(); Assert.Single(pending);
                Assert.Single(PanicSurfaces.All, x => x.Id == "keyword-comments").Stop(null);
                tube.PanicSilence(); pending.Dequeue()();
                Assert.False(tube.FindControl<Border>("SpeechBubble")!.IsVisible);
            }
            finally { CoreEntitlement.HasPremiumProvider = entitlement; s.CustomCompanionPhrases = phrases; ConditioningControlPanel.Avalonia.App.Ai = ai; }
        }
        finally
        {
            tube?.Close(); KeywordAvatarComment.CancelPending(); File.Delete(file);
            CoreSettings.Current.MasterVolume = volume;
            (CoreAudio.PlayStoppableProvider, KeywordAvatarComment.ScheduleRetry, KeywordAvatarComment.TubeSource) = saved;
        }
    });
}
public sealed class AwarenessRecentAppsTests
{
    private static string SourceRoot([System.Runtime.CompilerServices.CallerFilePath] string path = "") => System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path)!, "../.."));
    [Fact]
    public void RingKeepsEightDistinctNewestFirstAndIgnoresOwnUnknownAndFailedLookups()
    {
        var ring = new RecentForegroundApps();
        for (var i = 0; i < 10; i++) { var name = "app" + i; ring.Source = () => (name, false); ring.Sample(); }
        Assert.Equal(Enumerable.Range(2, 8).Reverse().Select(i => "app" + i), ring.Snapshot());
        ring.Source = () => ("APP3", false); ring.Sample();
        Assert.Equal("app3", ring.Snapshot()[0]);
        Assert.Equal(new[] { "app3", "app9", "app8", "app7", "app6", "app5", "app4", "app2" }, ring.Snapshot());
        var before = ring.Snapshot();
        ring.Source = () => ("ccp", true); ring.Sample();
        ring.Source = () => (null, false); ring.Sample();
        ring.Source = () => (" ", false); ring.Sample();
        ring.Source = () => throw new InvalidOperationException(); ring.Sample();
        Assert.Equal(before, ring.Snapshot());
    }
    [Fact]
    public Task InputSamplingGatesAccessThrottlesOnlyUnrestrictedAndCapturesRapidSwitches() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var engine = new KeywordAudioRuntime();
        var ring = engine.RecentApps;
        var s = CoreSettings.Current;
        var saved = (s.KeywordTriggersEnabled, s.KeywordTriggerAppScope, s.KeywordTriggerIgnoreOwnFocus,
            s.KeywordTriggerApps, s.KeywordTriggers, CoreEntitlement.HasPremiumProvider, CoreEntitlement.IsFreeTodayProvider);
        var now = new DateTime(2035, 1, 1);
        var calls = 0;
        string app = "firefox";
        ring.Source = () => { calls++; return (app, false); };
        engine.Now = () => now;
        try
        {
            CoreEntitlement.HasPremiumProvider = () => true;
            CoreEntitlement.IsFreeTodayProvider = _ => false;
            s.KeywordTriggersEnabled = true; s.KeywordTriggers = new();
            s.KeywordTriggerIgnoreOwnFocus = false;
            s.KeywordTriggerAppScope = AwarenessAppScope.Everywhere;
            engine.Key(KeywordInputKey.Clear); Assert.Equal(0, calls); // engine off
            engine.Start(); await Task.Delay(2200); Assert.Equal(0, calls); // no idle sampling
            engine.Key(KeywordInputKey.Clear); Assert.Equal(1, calls);
            app = "discord"; now = now.AddMilliseconds(100);
            engine.Character('x'); Assert.Equal(1, calls); // unrestricted throttle
            now = now.AddMilliseconds(1900);
            engine.Key(KeywordInputKey.Clear); Assert.Equal(2, calls); // exact boundary
            s.KeywordTriggerAppScope = AwarenessAppScope.OnlyListed;
            s.KeywordTriggerApps = new(); // even blocked apps are remembered, as WPF :582
            app = "steam"; now = now.AddMilliseconds(10); engine.Character('x');
            app = "zoom"; now = now.AddMilliseconds(10); engine.Key(KeywordInputKey.Clear);
            Assert.Equal(new[] { "zoom", "steam", "discord", "firefox" }, ring.Snapshot());
            s.KeywordTriggerAppScope = AwarenessAppScope.Everywhere;
            s.KeywordTriggerIgnoreOwnFocus = true;
            app = "teams"; engine.Key(KeywordInputKey.Clear); Assert.Equal(5, calls);
            CoreEntitlement.HasPremiumProvider = () => false;
            app = "private"; now = now.AddSeconds(3);
            engine.Key(KeywordInputKey.Clear); engine.Character('x'); Assert.Equal(5, calls);
            CoreEntitlement.IsFreeTodayProvider = _ => true;
            engine.Key(KeywordInputKey.Clear); Assert.Equal(6, calls);
            s.KeywordTriggersEnabled = false;
            engine.Character('x'); engine.Key(KeywordInputKey.Clear); Assert.Equal(6, calls);
            engine.Stop(); Assert.Equal("private", ring.Snapshot()[0]); // session ring retained
            engine.Start(); s.KeywordTriggersEnabled = true; s.KeywordTriggerAppScope = AwarenessAppScope.ExceptListed;
            s.KeywordTriggerApps = new() { "blocked" }; s.KeywordTriggers = new() { new() { Keyword = "q", CooldownSeconds = 0 } };
            app = "blocked"; var captured = await Task.Run(() => engine.CaptureInput()); Assert.Equal(7, calls);
            app = "allowed"; Dispatcher.UIThread.Post(() => engine.Character('q', snapshot: captured));
            Dispatcher.UIThread.RunJobs(); Assert.Equal(7, calls);
            Assert.Equal(DateTime.MinValue, s.KeywordTriggers[0].LastTriggeredAt); Assert.Equal("blocked", ring.Snapshot()[0]);
            s.KeywordTriggerAppScope = AwarenessAppScope.OnlyListed; s.KeywordTriggerApps = new() { "allowed" };
            ring.Source = () => { calls++; return (null, false); }; captured = await Task.Run(() => engine.CaptureInput());
            ring.Source = () => { calls++; return ("allowed", false); };
            Dispatcher.UIThread.Post(() => engine.Character('q', snapshot: captured)); Dispatcher.UIThread.RunJobs();
            Assert.Equal(8, calls); Assert.Equal(DateTime.MinValue, s.KeywordTriggers[0].LastTriggeredAt);
            CoreEntitlement.IsFreeTodayProvider = _ => false; Assert.Null(await Task.Run(() => engine.CaptureInput())); Assert.Equal(8, calls);
            Assert.Null(typeof(KeywordTriggerHead).GetField("AppSampler", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic));
        }
        finally
        {
            engine.Stop();
            (s.KeywordTriggersEnabled, s.KeywordTriggerAppScope, s.KeywordTriggerIgnoreOwnFocus,
                s.KeywordTriggerApps, s.KeywordTriggers, CoreEntitlement.HasPremiumProvider, CoreEntitlement.IsFreeTodayProvider) = saved;
        }
    });
    [Fact]
    public void SuggestionsRefreshOnShowModeCommitAndClickSavesCanonicalList() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current == null)
            AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>().UseSkia().WithInterFont().With(AppFonts.Options())
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var ring = KeywordTriggerHead.RecentApps;
        var source = ring.Source;
        var provider = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = service.Current;
        try
        {
            ring.Clear();
            ring.Source = () => ("firefox", false); ring.Sample();
            ring.Source = () => ("discord", false); ring.Sample();
            s.KeywordTriggerApps = new() { " DISCORD.exe " };
            s.KeywordTriggerAppScope = AwarenessAppScope.OnlyListed;
            var view = new AwarenessTabView();
            var panel = view.FindControl<WrapPanel>("AwarenessSeenAppsPanel")!;
            var label = view.FindControl<TextBlock>("TxtAwarenessSeenAppsLabel")!;
            Assert.True(label.IsVisible);
            var chip = Assert.IsType<Button>(Assert.Single(panel.Children));
            Assert.Equal("+ firefox", chip.Content);
            Assert.Equal("Hand", chip.Cursor?.ToString());
            Assert.Equal(string.Format(ConditioningControlPanel.Localization.Loc.Get("awareness_add_app_tip"), "firefox"), ToolTip.GetTip(chip));
            foreach (var file in System.IO.Directory.GetFiles(System.IO.Path.Combine(SourceRoot(), "CCP.Core/Localization/Languages"), "*.json"))
            {
                using var json = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(file));
                var tip = json.RootElement.GetProperty("awareness_add_app_tip").GetString()!;
                Assert.Contains("{0}", tip); Assert.NotEqual("awareness_add_app_tip", tip);
                Assert.Contains("firefox", string.Format(tip, "firefox"));
            }
            chip.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(new[] { " DISCORD.exe ", "firefox" }, s.KeywordTriggerApps);
            Assert.True(service.HasPendingSave);
            service.SaveImmediate();
            Assert.Equal(s.KeywordTriggerApps, new SettingsService().Current.KeywordTriggerApps);
            Assert.Empty(panel.Children); Assert.False(label.IsVisible);
            var box = view.FindControl<TextBox>("TxtAwarenessAppList")!;
            Assert.Equal(" DISCORD.exe , firefox", box.Text);
            box.Text = "firefox";
            box.RaiseEvent(new RoutedEventArgs(InputElement.LostFocusEvent));
            Assert.Equal("+ discord", Assert.IsType<Button>(Assert.Single(panel.Children)).Content);
            ring.Source = () => ("steam", false); ring.Sample();
            var mode = view.FindControl<ComboBox>("CmbAwarenessAppScope")!;
            mode.SelectedItem = mode.Items.OfType<ComboBoxItem>().Single(i => (string?)i.Tag == "ExceptListed");
            Assert.Equal(2, panel.Children.Count);
            ring.Source = () => ("zoom", false); ring.Sample();
            view.IsVisible = false; view.IsVisible = true;
            Assert.Equal(3, panel.Children.Count);
        }
        finally { SettingsService.DisarmAllPendingSaves(); CoreSettings.ServiceProvider = provider; ring.Clear(); ring.Source = source; }
    });
}
