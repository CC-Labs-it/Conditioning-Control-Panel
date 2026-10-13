// PORTED from ConditioningControlPanel/Services/Companion/BarkService.cs: the head half of the bark
// engine. The rules (match, gate, rotation) are CCP.Core BarkEngine; this file loads the active mod's
// rule set, seeds the CoreBark doorbell, wires the trigger sources this head has and speaks a pick
// through the avatar tube (whose PlaySpeechAudio owns master volume, the companion mute and
// voice-lines-muted; panic silences it through the "tube-speech" PanicSurfaces entry).
// ponytail: triggers whose sources are not on this head yet (lane barks-b): session engine
// (SessionStarted/Progress/Phase/Paused/Resumed/Refocused/Stopped/Completed, SessionSetupReady), webcam
// and gaze (FaceFound/FaceLost/Blink/LongStare/MouthOpen/TongueOut/TrackingStateChanged/GazePopped/
// GazeActiveChanged), video (VideoAboutToStart/Started/Ended, AttentionCheckPass/Fail), flash/subliminal/
// bubbles/bubble count/brain drain/mind wipe, progression (XP/LevelUp/CompanionLevelUp/Skill/Lucky/
// Achievement/Quest/Roadmap/Quiz/Mantra), lockdown, possession, emergency exit, descent, chaos run
// events beyond the three on CoreBark, activity/awareness, keyword triggers, remote, controller,
// tray, Discord/Patreon/update/tutorial, the Panic safety line, Idle chatter and AppOpened greeting.

using System;
using ConditioningControlPanel.Services.Bark;
using ConditioningControlPanel.Services.Companion;

namespace ConditioningControlPanel.Avalonia.Views.AvatarTube
{
    internal static class BarkHost
    {
        internal static BarkEngine? Engine { get; private set; }

        /// <summary>Where a decided bark goes: the avatar tube by default; tests capture it.</summary>
        internal static Action<BarkPick> Speaker { get; set; } = SpeakOnTube;

        /// <summary>WPF App startup Bark.Start + the CoreBark seeds (App.xaml.cs:316-349, :610). Idempotent.</summary>
        internal static void Start()
        {
            if (Engine != null) return;
            var engine = new BarkEngine
            {
                AvatarSpeaking = () => AvatarTubeWindow.Live?.IsSpeaking == true,
                AvatarBusy = ms => AvatarTubeWindow.Live?.IsCompanionBusy(ms) == true,
                EmiAvatarMuted = () => Windows.EmiDesk.EmiDeskService.Instance.AvatarMuted,
                TierName = () => (Platform.AccountSeed.Patreon?.CurrentTier ?? ConditioningControlPanel.Models.PatreonTier.None).ToString(),
                PoolRefPhrases = category => App.Mods?.GetPhrases(category),
            };
            try { engine.Load(BarkRuleLoader.Load()); }
            catch (Exception ex) { Serilog.Log.Error(ex, "BarkHost: failed to load rules"); }
            Engine = engine;

            CoreModsHooks.ReloadBarkRules = () => Engine?.Reload(BarkRuleLoader.Load());
            CoreBark.UiAction = a => { if (!string.IsNullOrEmpty(a)) Raise("UiAction", c => c.Set("action", a!)); };
            CoreBark.TabNavigated = t => { if (!string.IsNullOrEmpty(t)) Raise("TabNavigated", c => c.Set("tab", t!)); };
            CoreBark.FeatureOpened = f => { if (!string.IsNullOrEmpty(f)) Raise("FeatureOpened", c => c.Set("feature", f!)); };
            CoreBark.AvatarClicked = () =>
            {
                var clicks = Engine?.RegisterAvatarClick() ?? 0;
                Raise("AvatarClicked", c => c.Set("clicks_60s", (double)clicks));
            };
            CoreBark.ChaosDraftAutopick = () => Raise("ChaosDraftAutopick");
            CoreBark.ChaosResultsShown = (score, best, pbDelta, isPb, defused, detonated, bestCombo, difficulty) =>
                Raise("ChaosResultsShown", c => c
                    .Set("score", score).Set("best_score", best).Set("pb_delta", pbDelta).Set("is_pb", isPb)
                    .Set("defused", defused).Set("detonated", detonated).Set("best_combo", (double)bestCombo)
                    .Set("difficulty", difficulty)
                    .Set("runs_completed", (double)(ConditioningControlPanel.Services.Chaos.ChaosMeta.State?.RunsCompleted ?? 0))
                    .Set("rank", ConditioningControlPanel.Services.Chaos.ChaosRanks.NameLower(ConditioningControlPanel.Services.Chaos.ChaosMeta.RankIndex)));
            CoreBark.ChaosRankUp = rank => Raise("ChaosRankUp", c => c.Set("rank", rank));
            CoreBark.AllLinesProvider = AllLines;
            // WPF NotifySettingChanged (:357) minus the SessionSetupReady debounce (barks-b).
            CoreSettingsHooks.SettingChangedSink = name =>
            {
                if (string.IsNullOrEmpty(name) || !BarkEngine.TryGetSettingNumber(name!, out var value)) return;
                Raise("SettingChanged", c => c.Set("setting", name!).Set("value", value));
            };
            if (!_userMessageWired) { _userMessageWired = true; App.UserMessageSent += () => Engine?.NoteUserMessage(); }   // chat-suppression anchor
            Serilog.Log.Information("BarkHost started - {Count} rules", engine.Rules.Count);
        }

        private static bool _userMessageWired;

        /// <summary>WPF Stop/Dispose: the engine goes, the seams fall back to their unseeded no-ops.</summary>
        internal static void Stop()
        {
            Engine = null;
            CoreModsHooks.ReloadBarkRules = null;
            CoreBark.UiAction = null; CoreBark.TabNavigated = null; CoreBark.FeatureOpened = null;
            CoreBark.AvatarClicked = null; CoreBark.ChaosDraftAutopick = null; CoreBark.ChaosResultsShown = null;
            CoreBark.ChaosRankUp = null; CoreBark.AllLinesProvider = null;
            CoreSettingsHooks.SettingChangedSink = null;
        }

        /// <summary>Panic (PanicSurfaces "barks"): no new bark for the safety hold; the line in the air is cut by
        /// "tube-speech". ponytail: WPF also SPEAKS its Panic safety line here (MainWindow.xaml.cs:1755); barks-b.</summary>
        internal static void Panic() => Engine?.HoldFloor();

        /// <summary>WPF Raise: decide in Core, speak here. Never throws; no-op before Start.</summary>
        internal static bool Raise(string trigger, Action<BarkContext>? fill = null, bool guaranteed = false)
        {
            try
            {
                var engine = Engine;
                if (engine == null) return false;
                var ctx = new BarkContext(trigger);
                fill?.Invoke(ctx);
                var pick = engine.Decide(trigger, ctx, guaranteed);
                if (pick == null) return false;
                Speaker(pick);
                return true;
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "BarkHost: Raise('{Trigger}') failed", trigger);
                return false;
            }
        }

        /// <summary>WPF ResolveBarkAudio: the active mod's voiceline (builtin-ccp-default by default).</summary>
        internal static string? ResolveAudio(string? file)
        {
            if (string.IsNullOrWhiteSpace(file)) return null;
            var pick = ModCompanionContent.ResolveActive(CompanionChannel.BarkAudio, file);
            return pick.Found ? pick.Path : null;
        }

        /// <summary>WPF Speak (:1943): priority barks preempt (GigglePriority, logged to chat); ordinary
        /// barks and the mute egg go through Giggle (never logged, giggle sound one in five).</summary>
        private static void SpeakOnTube(BarkPick pick)
        {
            var tube = AvatarTubeWindow.Live;
            if (tube == null) { Serilog.Log.Debug("[BARK] no avatar window - dropping rule={Rule}", pick.Rule.Id); return; }
            SpeakOn(tube, pick);
        }

        internal static void SpeakOn(AvatarTubeWindow tube, BarkPick pick)
        {
            var line = BarkEngine.ApplySubstitutions(pick.Variant.Text, pick.Context);
            if (string.IsNullOrWhiteSpace(line)) return;

            // Mute egg: an easter_egg bark while master volume is 0 shows text only.
            if (pick.Rule.Class == BarkClass.EasterEgg && CoreSettings.Current.MasterVolume == 0)
            {
                tube.Giggle(line);   // PlaySpeechAudio's master==0 guard keeps it silent
                return;
            }
            var audio = ResolveAudio(pick.Variant.Audio);
            if (pick.Preempts)
                tube.GigglePriority(line, playSound: audio == null, aiGenerated: false,
                    phraseAudioPath: audio, barkVoice: audio != null, mood: pick.Rule.Mood);
            else
                tube.Giggle(line, phraseAudioPath: audio, barkVoice: audio != null);
        }

        private static System.Collections.Generic.IReadOnlyList<BarkLineInfo> AllLines()
        {
            var result = new System.Collections.Generic.List<BarkLineInfo>();
            var engine = Engine;
            if (engine == null) return result;
            foreach (var (rule, v) in engine.AllInlineLines())
            {
                var path = ResolveAudio(v.Audio);
                result.Add(new BarkLineInfo(ConditioningControlPanel.Services.CompanionPhraseIds.BarkLineId(rule.Id, v.Text, v.Audio), rule.Id, rule.Trigger,
                    v.Text, v.Audio, path != null ? System.IO.Path.GetDirectoryName(path) : null));
            }
            return result;
        }
    }
}
