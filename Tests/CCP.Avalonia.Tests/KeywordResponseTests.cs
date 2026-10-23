using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class KeywordResponseTests
{
    // No AppBuilder or windows: exercise the matcher and real Core seams without a font dependency.
    private static Task Run(Func<KeywordAudioRuntime, AppSettings, Task> body) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var s = CoreSettings.Current;
        var saved = (s.KeywordTriggers, s.KeywordTriggersEnabled, s.KeywordTriggerIgnoreOwnFocus,
            s.KeywordTriggerAppScope, s.KeywordGlobalCooldownSeconds, s.KeywordPerKeywordCooldownSeconds,
            CoreEntitlement.HasPremiumProvider, CoreEntitlement.IsFreeTodayProvider,
            CoreSession.IsSessionRunningProvider, CoreSession.IsEngineRunningProvider, CoreProgression.AddXPProvider,
            s.KeywordSessionMultiplier, s.AwarenessLoopProtectionEnabled, CoreAudio.PlayStoppableProvider, AvApp.Ai);
        var engine = new KeywordAudioRuntime();
        try
        {
            s.KeywordTriggersEnabled = true; s.KeywordTriggerIgnoreOwnFocus = false;
            s.KeywordTriggerAppScope = AwarenessAppScope.Everywhere;
            s.KeywordGlobalCooldownSeconds = 0; s.KeywordPerKeywordCooldownSeconds = 0;
            s.AwarenessLoopProtectionEnabled = false; s.KeywordSessionMultiplier = 2;
            s.KeywordTriggers = new() { new() { Keyword = "sit", Actions = new() { new AddXpAction { Amount = 7 } } } };
            CoreEntitlement.HasPremiumProvider = () => true; CoreEntitlement.IsFreeTodayProvider = _ => false;
            CoreSession.IsSessionRunningProvider = () => true; CoreSession.IsEngineRunningProvider = () => true;
            CoreProgression.AddXPProvider = null; AvApp.Ai = null;
            engine.Start();
            await body(engine, s);
        }
        finally
        {
            engine.Stop();
            (s.KeywordTriggers, s.KeywordTriggersEnabled, s.KeywordTriggerIgnoreOwnFocus,
                s.KeywordTriggerAppScope, s.KeywordGlobalCooldownSeconds, s.KeywordPerKeywordCooldownSeconds,
                CoreEntitlement.HasPremiumProvider, CoreEntitlement.IsFreeTodayProvider,
                CoreSession.IsSessionRunningProvider, CoreSession.IsEngineRunningProvider, CoreProgression.AddXPProvider,
                s.KeywordSessionMultiplier, s.AwarenessLoopProtectionEnabled, CoreAudio.PlayStoppableProvider, AvApp.Ai) = saved;
        }
    });

    private static void Type(KeywordAudioRuntime engine, string text = "sit") { foreach (var ch in text) engine.Character(ch); }

    [Fact]
    public Task TypedTriggerAwardsXpWithSessionMultiplier() => Run((engine, _) =>
    {
        var awards = new List<(double, string)>();
        CoreProgression.AddXPProvider = (amount, source) => awards.Add((amount, source));
        Type(engine);
        Assert.Equal((14d, "KeywordTrigger"), Assert.Single(awards));
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(7, 7)]
    [InlineData(0, 0)]
    [InlineData(-1, 0)]
    public Task XpWithoutSessionUsesPositiveBaseAmountOnly(int amount, int expected) => Run((engine, s) =>
    {
        CoreSession.IsSessionRunningProvider = () => false;
        s.KeywordTriggers[0].Actions = new() { new AddXpAction { Amount = amount } };
        double awarded = 0;
        CoreProgression.AddXPProvider = (value, _) => awarded += value;
        Type(engine); Assert.Equal(expected, awarded);
        return Task.CompletedTask;
    });

    [Fact]
    public Task StartIsIdempotentAndDoesNotDiscardPartialTyping() => Run((engine, _) =>
    {
        var fired = 0; engine.TriggerFired += _ => fired++;
        Type(engine, "si"); engine.Start(); engine.Character('t');
        Assert.Equal(1, fired);
        engine.Stop(); CoreEntitlement.HasPremiumProvider = () => false; engine.Start();
        Assert.False(engine.IsActive);
        return Task.CompletedTask;
    });

    [Fact]
    public Task DemoIsEntitledUnarmedAndKeepsBoundedSnapshotsWithoutEditingTriggers() => Run((engine, s) =>
    {
        engine.Stop(); s.KeywordTriggersEnabled = false;
        CoreEntitlement.HasPremiumProvider = () => false; engine.FireDemo("sit", "Tutorial");
        Assert.Empty(engine.RecentFires);
        CoreEntitlement.IsFreeTodayProvider = feature => feature == "awareness";
        var events = new List<KeywordTrigger>(); engine.TriggerFired += events.Add;
        for (var i = 0; i < 21; i++) engine.FireDemo("demo " + i, "Tutorial");
        Assert.Equal(21, events.Count); Assert.Equal(20, engine.RecentFires.Count);
        var latest = engine.RecentFires[0];
        Assert.Equal("demo 20", latest.Keyword); Assert.Equal("Tutorial", latest.Source);
        Assert.Equal(new[] { "Highlight", "AvatarComment" }, latest.ActionKeys);
        events.Last().Actions.Clear(); Assert.Equal(2, latest.ActionKeys.Count);
        Assert.Single(s.KeywordTriggers); Assert.Equal(DateTime.MinValue, s.KeywordTriggers[0].LastTriggeredAt);
        return Task.CompletedTask;
    });

    [Fact]
    public Task EngineOffHoldsAudioWhileXpStillRuns() => Run((engine, s) =>
    {
        var file = Path.GetTempFileName();
        try
        {
            CoreSession.IsSessionRunningProvider = () => false; CoreSession.IsEngineRunningProvider = () => false;
            var plays = 0; double xp = 0;
            CoreAudio.PlayStoppableProvider = (_, _, _, _, _) => { plays++; return () => { }; };
            CoreProgression.AddXPProvider = (value, _) => xp += value;
            s.KeywordTriggers[0].Actions.Insert(0, new PlayAudioAction { FilePath = file });
            Type(engine); Assert.Equal(0, plays); Assert.Equal(7, xp);
        }
        finally { File.Delete(file); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task PanicCancelsActionsWaitingBehindAudioRepeats() => Run(async (engine, s) =>
    {
        var file = Path.GetTempFileName();
        try
        {
            var gap = new TaskCompletionSource(); var plays = 0; var stops = 0; var xp = 0d;
            engine.Delay = (_, _) => gap.Task;
            CoreAudio.PlayStoppableProvider = (_, _, _, _, _) => { plays++; return () => stops++; };
            CoreProgression.AddXPProvider = (value, _) => xp += value;
            s.KeywordTriggers[0].Actions.Insert(0, new PlayAudioAction { FilePath = file, PlayCount = 2, DelayBetweenMs = 200 });
            Type(engine); Assert.Equal(1, plays); Assert.Equal(0, xp);
            engine.StopAudio(); gap.SetResult(); await Task.Yield();
            Assert.Equal(1, plays); Assert.Equal(1, stops); Assert.Equal(0, xp);
            Assert.True(engine.IsActive);
        }
        finally { File.Delete(file); }
    });

    [Fact]
    public Task RepeatsFinishBeforeLaterActionsRun() => Run(async (engine, s) =>
    {
        var file = Path.GetTempFileName();
        try
        {
            var gap = new TaskCompletionSource(); var order = new List<string>();
            engine.Delay = (_, _) => gap.Task;
            CoreAudio.PlayStoppableProvider = (_, _, _, _, _) => { order.Add("audio"); return () => { }; };
            CoreProgression.AddXPProvider = (_, _) => order.Add("xp");
            s.KeywordTriggers[0].Actions.Insert(0, new PlayAudioAction { FilePath = file, PlayCount = 2, DelayBetweenMs = 200 });
            Type(engine); Assert.Equal(new[] { "audio" }, order);
            gap.SetResult(); await Task.Yield();
            Assert.Equal(new[] { "audio", "audio", "xp" }, order);
        }
        finally { File.Delete(file); }
    });

    [Fact]
    public Task ExactSubliminalHardMutesEvenWithLoopProtectionDisabled() => Run((engine, s) =>
    {
        var now = new DateTime(2026, 10, 23, 12, 0, 0); engine.Now = () => now;
        s.KeywordTriggers[0].CooldownSeconds = 1;
        s.KeywordTriggers[0].Actions = new() { new VisualEffectAction { Effect = KeywordVisualEffect.ExactSubliminal } };
        Type(engine); now = now.AddSeconds(2); Type(engine);
        Assert.Single(engine.RecentFires);
        now = now.AddSeconds(2); engine.Key(KeywordInputKey.Clear); Type(engine);
        Assert.Equal(2, engine.RecentFires.Count);
        return Task.CompletedTask;
    });

    [Fact]
    public void MindWipeRequiresAClipAndRunningEngineOrSession()
    {
        var saved = (CoreMindWipe.ClipCountProvider, CoreMindWipe.TriggerOnceProvider,
            CoreSession.IsEngineRunningProvider, CoreSession.IsSessionRunningProvider);
        try
        {
            var clips = 0; var engine = false; var session = false; var fired = 0;
            CoreMindWipe.ClipCountProvider = () => clips; CoreMindWipe.TriggerOnceProvider = () => fired++;
            CoreSession.IsEngineRunningProvider = () => engine; CoreSession.IsSessionRunningProvider = () => session;
            void Fire() => KeywordResponses.Visual(KeywordVisualEffect.MindWipe, new KeywordTrigger(), () => { });
            Fire(); clips = 1; Fire(); Assert.Equal(0, fired);
            engine = true; Fire(); engine = false; session = true; Fire(); Assert.Equal(2, fired);
            clips = 0; Fire(); Assert.Equal(2, fired);
        }
        finally { (CoreMindWipe.ClipCountProvider, CoreMindWipe.TriggerOnceProvider,
            CoreSession.IsEngineRunningProvider, CoreSession.IsSessionRunningProvider) = saved; }
    }

    [Fact]
    public void CannedCommentUsesEnabledCustomPhrasePool()
    {
        var s = CoreSettings.Current; var saved = s.CustomCompanionPhrases;
        try
        {
            s.CustomCompanionPhrases = new() { new() { Category = "keyword-test", Text = "enabled", Enabled = true },
                new() { Category = "keyword-test", Text = "disabled", Enabled = false } };
            Assert.Equal("enabled", KeywordResponses.Canned("keyword-test"));
            Assert.Null(KeywordResponses.Canned(null));
        }
        finally { s.CustomCompanionPhrases = saved; }
    }

    [Fact]
    public Task KeywordHapticUsesOwnIntensityWithSubliminalRoutingOff() => Run(async (engine, s) =>
    {
        await Task.CompletedTask;
        HapticsHeadTests.WithHaptics(true, true, (haptics, toasts) =>
        {
            var rule = haptics.Settings.V2.Rule(ConditioningControlPanel.Services.Haptics.Core.HapticEventKind.KeywordTrigger);
            var subliminal = haptics.Settings.SubliminalEnabled;
            var saved = (rule.Enabled, rule.Intensity);
            try
            {
                haptics.Settings.Enabled = true; haptics.Settings.SubliminalEnabled = false;
                rule.Enabled = true; rule.Intensity = 1;
                Assert.True(haptics.ConnectAsync().GetAwaiter().GetResult());
                s.KeywordTriggers[0].Actions = new() { new HapticAction { Intensity = 0.5 } };
                Type(engine);
                Assert.True(System.Threading.SpinWait.SpinUntil(() => toasts.Any(t => t.Contains('%')), 3000));
            }
            finally { (rule.Enabled, rule.Intensity) = saved; haptics.Settings.SubliminalEnabled = subliminal; }
        });
    });

}
