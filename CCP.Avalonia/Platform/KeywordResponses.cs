using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Awareness;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

internal sealed partial class KeywordAudioRuntime
{
    private readonly LinkedList<TriggerFireRecord> _recent = new();
    internal IReadOnlyList<TriggerFireRecord> RecentFires => _recent.ToArray();
    internal void ClearRecentFires() => _recent.Clear();
    internal event Action<KeywordTrigger>? TriggerFired;

    private void Record(KeywordTrigger trigger, string source)
    {
        _cooldowns.Record(trigger, CoreSettings.Current, Now().ToUniversalTime());
        _recent.AddFirst(new TriggerFireRecord
        {
            Keyword = trigger.Keyword, TriggerId = trigger.Id, VisualEffect = trigger.VisualEffect,
            Source = source, FiredAt = Now(), ActionKeys = trigger.Actions.Where(a => a != null && a.Enabled && a is not AddXpAction)
                .Select(KeywordResponseRules.ActionKey).ToList(),
        });
        while (_recent.Count > 20) _recent.RemoveLast();
        TriggerFired?.Invoke(trigger);
        App.Quests?.TrackKeywordTrigger();
        if (App.Achievements is { } achievements)
        {
            achievements.Progress.KeywordTriggersFired++;
            achievements.IsDirty = true;
            achievements.TryUnlockExclusive("magic_word");
            if (achievements.Progress.KeywordTriggersFired >= AchievementRules.PavlovKeywordTriggers)
                achievements.TryUnlockExclusive("pavlov");
        }
    }

    /// <summary>The latest response chain; tests await it instead of yielding (P57).</summary>
    internal Task LastDispatch { get; private set; } = Task.CompletedTask;

    private async Task Dispatch(IReadOnlyList<KeywordTrigger> fired)
    {
        var token = _pending.Token;
        try
        {
            foreach (var action in KeywordResponseRules.Actions(fired))
            {
                token.ThrowIfCancellationRequested();
                if (action is PlayAudioAction audio) await Play(audio);
                else await KeywordResponses.Run(action, fired[0], token,
                    () => _cooldowns.Mute(fired[0].Keyword, 3000, Now().ToUniversalTime()));
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Log.Warning(ex, "Keyword response failed"); }
    }

    internal void FireDemo(string keyword, string source)
    {
        if (string.IsNullOrWhiteSpace(keyword) || !HasAccess) return;
        var demo = new KeywordTrigger
        {
            Id = "tutorial:demo", Keyword = keyword, VisualEffect = KeywordVisualEffect.HighlightOnly,
            Actions = new() { new HighlightAction(), new AvatarCommentAction
                { FallbackPhraseCategory = "PuppyPraise", RequireAiAvailable = false } },
        };
        Record(demo, source);
        LastDispatch = Dispatch(new[] { demo });
    }
}

/// <summary>Adapted from parity/trunk a4ee52d40; retains this head's stoppable audio and existing effect door.</summary>
internal static class KeywordResponses
{
    internal static Task Run(KeywordAction action, KeywordTrigger trigger, CancellationToken token, Action muteEcho)
    {
        switch (action)
        {
            case VisualEffectAction visual: Visual(visual.Effect, trigger, muteEcho); break;
            case HapticAction haptic:
                _ = CoreHaptics.Service?.TriggerKeywordPatternAsync(trigger.Keyword, haptic.Intensity); break;
            case AddXpAction xp when xp.Amount > 0:
                CoreProgression.AddXP(xp.Amount * (CoreSession.IsSessionRunning ? CoreSettings.Current.KeywordSessionMultiplier : 1), "KeywordTrigger"); break;
            case AvatarCommentAction comment: _ = Comment(comment, trigger.Keyword, token); break;
            case ChasterAddTimeAction chaster: Book(chaster.Minutes, trigger); break;
            // Highlight needs OCR word boxes; ExtendSession is a no-op in the WPF reference too.
        }
        return Task.CompletedTask;
    }

    internal static void Visual(KeywordVisualEffect effect, KeywordTrigger trigger, Action muteEcho)
    {
        void Overlay(Action<global::Avalonia.Controls.Window> show)
        {
            if (!X11Overlay.IsAvailable || MainShellWindow.Current is not { } host) return;
            CompanionEffects.StartEffect(() => show(host));
        }
        switch (effect)
        {
            case KeywordVisualEffect.SubliminalFlash:
                // WPF :1789 App.Subliminal.FlashSubliminal: the scheduler's show path (whisper, haptic, XP).
                if (CoreSubliminal.PickPhrase() is { } phrase) CoreSubliminal.ShowProvider?.Invoke(phrase);
                break;
            case KeywordVisualEffect.ExactSubliminal:
                Overlay(h => SubliminalOverlay.Show(h, trigger.Keyword.ToUpperInvariant()));
                muteEcho(); break;
            case KeywordVisualEffect.ImageFlash: Overlay(h => FlashOverlay.TriggerOnce(h)); break;
            case KeywordVisualEffect.MindWipe:
                if ((CoreSession.IsEngineRunning || CoreSession.IsSessionRunning) && CoreMindWipe.ClipCount > 0) CoreMindWipe.TriggerOnce();
                break;
            case KeywordVisualEffect.Bubbles: BubbleOverlay.Spawn(); break;
            // ponytail: OverlayPulse, BrainDrain and engine-off SpawnOnce need their unported overlay APIs.
        }
    }

    internal static string? Canned(string? category)
    {
        if (string.IsNullOrEmpty(category)) return null;
        var phrases = AwarenessReactionPhrases.Enabled(category, Views.Dialogs.CompanionPhraseEditorDialog.CategoryNames.Contains(category));
        if (phrases.Length == 0) phrases = CoreMods.GetPhrases(category) ?? Array.Empty<string>();
        return phrases.Length == 0 ? null : phrases[Random.Shared.Next(phrases.Length)];
    }

    private static async Task Comment(AvatarCommentAction comment, string keyword, CancellationToken token)
    {
        try
        {
            var ai = App.Ai;
            string? line = null;
            if (ai?.IsAvailable == true) line = await ai.GetKeywordCommentAsync(keyword, comment.PromptTemplate);
            var fromAi = !string.IsNullOrEmpty(line);
            if (string.IsNullOrEmpty(line)) line = Canned(comment.FallbackPhraseCategory);
            if (!string.IsNullOrEmpty(line)) await Dispatcher.UIThread.InvokeAsync(() => ShowLine(line, fromAi, token));
        }
        catch (Exception ex) { Log.Debug(ex, "Keyword comment failed"); }
    }

    private static void ShowLine(string line, bool fromAi, CancellationToken token, int attempt = 0)
    {
        if (token.IsCancellationRequested || AvatarTubeWindow.Live is not { } tube) return;
        if (tube.IsSpeaking)
        {
            if (attempt < 16) DispatcherTimer.RunOnce(() => ShowLine(line, fromAi, token, attempt + 1), TimeSpan.FromMilliseconds(750));
            return;
        }
        tube.RunOnAvatar(() =>
        {
            if (token.IsCancellationRequested) return;
            tube.GigglePriority(line, playSound: true, aiGenerated: fromAi);
            // ponytail: Awareness v2 arbiter routing is not on this head yet.
        });
    }

    private static KeywordTriggerChasterCap? _cap;
    internal static int Book(int minutes, KeywordTrigger trigger)
    {
        var service = ChasterHead.Service;
        if (service == null) return 0;
        var path = Path.Combine(CorePaths.UserData, "chaster_trigger_caps.json");
        _cap ??= KeywordTriggerChasterCap.Load(path);
        var id = string.IsNullOrEmpty(trigger.Id) ? "keyword:" + trigger.Keyword : trigger.Id;
        var now = DateTime.Now;
        var allowed = _cap.Allowance(id, Math.Clamp(minutes, 0, 60) * 60, now);
        if (allowed <= 0) return 0;
        var applied = service.NoteSeconds("watcher", allowed).AppliedSeconds;
        if (applied > 0) { _cap.Record(id, applied, now); _cap.Save(path); }
        return applied;
    }
}
