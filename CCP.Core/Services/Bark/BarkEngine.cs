using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ConditioningControlPanel.Services.Companion;
using ConditioningControlPanel.Services.Possession;

namespace ConditioningControlPanel.Services.Bark
{
    /// <summary>A bark the engine decided to speak: the rule, the chosen line and the fire's context.</summary>
    public sealed record BarkPick(BarkRule Rule, BarkVariant Variant, BarkContext Context)
    {
        /// <summary>WPF Speak: non-Normal class or priority &gt;= 100 preempts (GigglePriority).</summary>
        public bool Preempts => Rule.Class != BarkClass.Normal || Rule.Priority >= BarkEngine.PriorityBarkThreshold;
    }

    /// <summary>
    /// The bark decision engine: condition match, gate (one-shot latch, safety hold, chat suppression,
    /// global min-gap, per-rule cooldown, chance), no-repeat variant/idle rotation and commit. A
    /// faithful extraction of ConditioningControlPanel/Services/Companion/BarkService.cs (Raise :1097,
    /// DispatchIdle :1178, ConditionPass :1379, EvaluateGate :1645, CommitFire :1793, PickVariant :1867):
    /// same order of checks, same constants. Everything the head owns (the avatar, whisper audio,
    /// the narrator, live service reads) comes in through the Func seams below; unseeded they answer
    /// "not busy / unknown", and settings come from <see cref="CoreSettings"/>.
    /// <para>ponytail: lane barks-wpf - WPF BarkService still carries its own copy of this logic and
    /// should delegate here (its tests only run on the Windows rgate job). Until then both copies are
    /// pinned to the same behaviour by Tests/CCP.Core.Tests/BarkEngineTests.cs.</para>
    /// </summary>
    public sealed class BarkEngine
    {
        public const int GlobalMinGapMs = 60000;
        public const int SafetyHoldMs = 6000;
        public const int PriorityBarkThreshold = 100;
        public const int RecentlySpokenMemory = 8;
        public const double GatedIdleBias = 0.35;
        public const string EmergencyExitOpenedTrigger = "EmergencyExitOpened";
        public const string EmergencyExitVerdictTrigger = "EmergencyExitVerdict";
        private static readonly TimeSpan RapidClickWindow = TimeSpan.FromSeconds(60);

        /// <summary>WPF GlobalGapExemptTriggers: functional triggers skip ONLY the global min-gap.</summary>
        public static readonly HashSet<string> GlobalGapExemptTriggers = new(StringComparer.OrdinalIgnoreCase)
        {
            "AttentionCheckFail",
            PossessionBarkTriggers.Effect, PossessionBarkTriggers.Tripwire, PossessionBarkTriggers.Warden,
            PossessionBarkTriggers.RungChanged, PossessionBarkTriggers.TimerRestarted, PossessionBarkTriggers.Remember,
            EmergencyExitOpenedTrigger, EmergencyExitVerdictTrigger,
            PossessionBarkTriggers.Conscript,
        };

        private static readonly HashSet<string> TextOnlyAttribution = new(StringComparer.OrdinalIgnoreCase)
        {
            PossessionBarkTriggers.Effect, PossessionBarkTriggers.Tripwire, PossessionBarkTriggers.Warden,
            PossessionBarkTriggers.RungChanged, PossessionBarkTriggers.TimerRestarted, PossessionBarkTriggers.Remember,
            PossessionBarkTriggers.Conscript, EmergencyExitOpenedTrigger, EmergencyExitVerdictTrigger,
        };

        private readonly object _gate = new();
        private readonly TimeProvider _clock;
        private readonly Random _rng;
        private BarkRuleSet _rules = BarkRuleSet.Empty;
        private readonly Dictionary<string, DateTime> _lastFiredUtc = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _firedOnceSession = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, HashSet<string>> _usedVariantKeys = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _lastVariantKey = new(StringComparer.OrdinalIgnoreCase);
        private readonly Queue<string> _recentlySpoken = new();
        private readonly HashSet<string> _recentlySpokenSet = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _usedIdleRules = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<DateTime> _avatarClicks = new();
        private string? _lastIdleRuleId;
        private DateTime _globalLastFireUtc = DateTime.MinValue;
        private DateTime _safetyHoldUntilUtc = DateTime.MinValue;
        private DateTime _lastUserMessageUtc = DateTime.MinValue;

        // ---- head seams (null = unknown / not busy) ----
        /// <summary>A live head read for a condition field (video_playing, webcam_running…); null = not known here.</summary>
        public Func<string, object?>? LiveField { get; set; }
        public Func<bool>? EmiAvatarMuted { get; set; }
        public Func<bool>? WhisperPlaying { get; set; }
        public Func<bool>? NarratorPlaying { get; set; }
        public Func<bool>? AvatarSpeaking { get; set; }
        /// <summary>WPF AvatarWindow.IsCompanionBusy(windowMs): an AI reply in flight or shown within the window.</summary>
        public Func<int, bool>? AvatarBusy { get; set; }
        /// <summary>The current Patreon tier name, for tier-scoped latches ("id@Tier").</summary>
        public Func<string>? TierName { get; set; }
        /// <summary>A pool_ref rule's phrases (WPF App.Mods.GetPhrases).</summary>
        public Func<string, string[]?>? PoolRefPhrases { get; set; }

        /// <summary>The settings the gate reads and the rotation persists into (tests pass their own).</summary>
        public Func<ConditioningControlPanel.Models.AppSettings> Settings { get; set; } = () => CoreSettings.Current;
        public Action SaveSettings { get; set; } = () => CoreSettings.Save(suppressCloudBackup: true);

        public bool DryRun { get; set; }

        public BarkEngine(TimeProvider? clock = null, Random? rng = null)
        {
            _clock = clock ?? TimeProvider.System;
            _rng = rng ?? new Random();
        }

        private DateTime Now => _clock.GetUtcNow().UtcDateTime;

        public BarkRuleSet Rules { get { lock (_gate) return _rules; } }

        /// <summary>WPF Start: rules plus the persisted rotation.</summary>
        public void Load(BarkRuleSet rules)
        {
            lock (_gate)
            {
                _rules = rules;
                var s = Settings();
                _usedVariantKeys.Clear();
                foreach (var kv in s.BarkVariantRotation)
                    _usedVariantKeys[kv.Key] = new HashSet<string>(kv.Value ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
                _usedIdleRules.Clear();
                foreach (var id in s.BarkIdleRotation) _usedIdleRules.Add(id);
            }
        }

        /// <summary>WPF ReloadRules: a mod switch re-arms session one-shots and cooldowns; rotation stays.</summary>
        public void Reload(BarkRuleSet rules)
        {
            lock (_gate)
            {
                _rules = rules;
                _firedOnceSession.Clear();
                _lastFiredUtc.Clear();
            }
        }

        /// <summary>WPF NotifyExternalLineSpoken: another mouth spoke; the global min-gap restarts.</summary>
        public void NoteExternalLineSpoken() { lock (_gate) _globalLastFireUtc = Now; }

        /// <summary>Panic: nothing but a safety bark speaks for <see cref="SafetyHoldMs"/> (the hold WPF's
        /// Panic safety bark sets in CommitFire).</summary>
        public void HoldFloor() { lock (_gate) _safetyHoldUntilUtc = Now.AddMilliseconds(SafetyHoldMs); }

        /// <summary>WPF UserMessageSent: anchors chat suppression.</summary>
        public void NoteUserMessage() { lock (_gate) _lastUserMessageUtc = Now; }

        /// <summary>WPF BarkState.RegisterAvatarClick + AvatarClicksWithin(60 s).</summary>
        public int RegisterAvatarClick()
        {
            lock (_gate)
            {
                var now = Now;
                _avatarClicks.Add(now);
                if (_avatarClicks.Count > 256) _avatarClicks.RemoveRange(0, _avatarClicks.Count - 256);
                return _avatarClicks.Count(t => t >= now - RapidClickWindow);
            }
        }

        /// <summary>WPF Raise: the first rule (priority order) whose conditions pass, through the gate.</summary>
        public BarkPick? Decide(string trigger, BarkContext ctx, bool guaranteed = false)
        {
            lock (_gate)
            {
                var rules = _rules.ForTrigger(trigger);
                BarkRule? winner = rules.FirstOrDefault(r => ConditionsPass(r, ctx));
                return winner == null ? null : DecideLocked(winner, ctx, guaranteed);
            }
        }

        /// <summary>WPF DispatchIdle (the mute and speaking checks stay with the caller).</summary>
        public BarkPick? DecideIdle()
        {
            var ctx = new BarkContext("Idle");
            lock (_gate)
            {
                var eligible = _rules.ForTrigger("Idle").Where(r => ConditionsPass(r, ctx)).ToList();
                if (eligible.Count == 0) return null;
                var unused = eligible.Where(r => !_usedIdleRules.Contains(r.Id)).ToList();
                if (unused.Count == 0)
                {
                    _usedIdleRules.Clear();
                    if (_lastIdleRuleId != null) _usedIdleRules.Add(_lastIdleRuleId);
                    PersistIdleRotation();
                    unused = eligible.Where(r => !_usedIdleRules.Contains(r.Id)).ToList();
                    if (unused.Count == 0) unused = eligible;
                }
                var gated = unused.Where(r => r.Conditions != null && r.Conditions.Count > 0).ToList();
                var winner = gated.Count > 0 && _rng.NextDouble() < GatedIdleBias
                    ? gated[_rng.Next(gated.Count)]
                    : unused[_rng.Next(unused.Count)];
                return DecideLocked(winner, ctx, guaranteed: false);
            }
        }

        private BarkPick? DecideLocked(BarkRule winner, BarkContext ctx, bool guaranteed)
        {
            var pool = ResolvePool(winner);
            var (fire, idx, reason) = EvaluateGate(winner, pool, guaranteed);
            if (!fire)
            {
                Serilog.Log.Debug("[BARK] blocked trigger={Trigger} rule={Rule} reason={Reason}", ctx.Trigger, winner.Id, reason);
                return null;
            }
            CommitFire(winner, idx, pool);
            Serilog.Log.Information("[BARK] {Verb} trigger={Trigger} rule={Rule} variant#={Idx}",
                DryRun ? "WOULD FIRE" : "FIRE", ctx.Trigger, winner.Id, idx);
            return DryRun ? null : new BarkPick(winner, pool[idx], ctx);
        }

        // ===================== conditions (WPF :1369-1520) =====================

        private bool ConditionsPass(BarkRule rule, BarkContext ctx)
        {
            if (rule.Conditions == null || rule.Conditions.Count == 0) return true;
            foreach (var kvp in rule.Conditions)
                if (!ConditionPass(kvp.Key, kvp.Value, ctx)) return false;
            return true;
        }

        private bool ConditionPass(string key, object? expected, BarkContext ctx)
        {
            string field = key;
            string op = "eq";
            foreach (var suffix in new[] { "_gte", "_lte", "_gt", "_lt", "_eq" })
            {
                if (key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    field = key.Substring(0, key.Length - suffix.Length);
                    op = suffix.Substring(1);
                    break;
                }
            }

            var actual = ResolveField(field, ctx);
            if (actual == null) return false;

            if (op == "eq")
            {
                if (expected is bool eb && TryBool(actual, out var ab)) return ab == eb;
                if (TryDouble(expected, out var en) && TryDouble(actual, out var an))
                    return Math.Abs(an - en) < 0.0001;
                return string.Equals(actual.ToString(), expected?.ToString(), StringComparison.OrdinalIgnoreCase);
            }

            if (!TryDouble(actual, out var a) || !TryDouble(expected, out var e)) return false;
            return op switch
            {
                "gte" => a >= e,
                "lte" => a <= e,
                "gt" => a > e,
                "lt" => a < e,
                _ => false
            };
        }

        /// <summary>The settings/time fields of WPF ResolveField; the head's live reads; else the context.</summary>
        private object? ResolveField(string field, BarkContext ctx)
        {
            var s = Settings();
            var local = _clock.GetLocalNow();
            switch (field.ToLowerInvariant())
            {
                case "clicks_60s": return (double)_avatarClicks.Count(t => t >= Now - RapidClickWindow);
                case "master_volume": return (double)s.MasterVolume;
                case "mute": return s.MasterVolume == 0;
                case "player_level": return (double)s.PlayerLevel;
                case "total_sessions": return (double)s.TotalSessions;
                case "daily_quest_streak": return (double)s.DailyQuestStreak;
                case "current_streak": return (double)s.CurrentStreak;
                case "is_nye": return (local.Month == 12 && local.Day == 31) || (local.Month == 1 && local.Day == 1);
                case "local_hour": return (double)local.Hour;
                case "sessions_7d": return (double)s.SessionsWithinDays(7);
                case "late_sessions_7d": return (double)s.LateSessionsWithinDays(7);
                case "sessions_today": return (double)s.SessionsToday();
                case "same_mod_run": return (double)s.SameModRun;
                case "is_late_hour": { int h = local.Hour; return h >= 23 || h < 4; }
            }
            object? live = null;
            try { live = LiveField?.Invoke(field.ToLowerInvariant()); } catch { }
            if (live != null) return live;
            return ctx.Values.TryGetValue(field, out var v) ? v : null;
        }

        private static bool TryDouble(object? raw, out double value)
        {
            value = 0;
            switch (raw)
            {
                case null: return false;
                case double d: value = d; return true;
                case int i: value = i; return true;
                case long l: value = l; return true;
                case float f: value = f; return true;
                case bool b: value = b ? 1 : 0; return true;
                case string str when double.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out var p):
                    value = p; return true;
                default: return false;
            }
        }

        private static bool TryBool(object? raw, out bool value)
        {
            value = false;
            switch (raw)
            {
                case bool b: value = b; return true;
                case int i: value = i != 0; return true;
                case double d: value = Math.Abs(d) > double.Epsilon; return true;
                case string str when bool.TryParse(str, out var p): value = p; return true;
                default: return false;
            }
        }

        // ===================== pool (WPF ResolvePool :1522) =====================

        private List<BarkVariant> ResolvePool(BarkRule rule)
        {
            if (rule.VariantPool != null && rule.VariantPool.Count > 0)
                return rule.VariantPool.Where(v => IsBarkLineEnabled(rule.Id, v)).ToList();
            if (!string.IsNullOrWhiteSpace(rule.PoolRef))
            {
                string[]? phrases = null;
                try { phrases = PoolRefPhrases?.Invoke(rule.PoolRef!); } catch { }
                if (phrases != null && phrases.Length > 0)
                    return phrases.Select(p => new BarkVariant(p)).ToList();
            }
            return new List<BarkVariant>();
        }

        private bool IsBarkLineEnabled(string ruleId, BarkVariant v)
        {
            var s = Settings();
            var id = CompanionPhraseIds.BarkLineId(ruleId, v.Text, v.Audio);
            return !s.DisabledPhraseIds.Contains(id) && !s.RemovedPhraseIds.Contains(id);
        }

        /// <summary>WPF GetAllBarkLines (Phrase Manager), minus the folder (resolved by the caller).</summary>
        public IReadOnlyList<(BarkRule Rule, BarkVariant Variant)> AllInlineLines()
        {
            var result = new List<(BarkRule, BarkVariant)>();
            foreach (var rule in Rules.AllRules)
            {
                if (rule.VariantPool == null || rule.VariantPool.Count == 0) continue;
                foreach (var v in rule.VariantPool) if (v.HasText) result.Add((rule, v));
            }
            return result;
        }

        // ===================== gate (WPF EvaluateGate :1645) =====================

        private static bool Ask(Func<bool>? f) { try { return f?.Invoke() == true; } catch { return false; } }

        private (bool Fire, int Index, string Reason) EvaluateGate(BarkRule rule, List<BarkVariant> pool, bool guaranteed)
        {
            if (pool.Count == 0) return (false, -1, "empty-pool");

            bool isSafety = rule.Class == BarkClass.Safety;
            bool bypass = isSafety || guaranteed;
            var s = Settings();

            if (!isSafety && Ask(EmiAvatarMuted)) return (false, -1, "emi-desk-mute");
            if (!isSafety && !s.AvatarEnabled) return (false, -1, "companion-off");
            if (!isSafety && !rule.Repeatable && AlreadyFiredOnce(rule)) return (false, -1, $"already-fired ({rule.Scope})");

            if (!bypass)
            {
                var now = Now;
                if (now < _safetyHoldUntilUtc) return (false, -1, "safety-active");
                if (Ask(WhisperPlaying) && !TextOnlyAttribution.Contains(rule.Trigger)) return (false, -1, "whisper-active");
                if (Ask(NarratorPlaying)) return (false, -1, "narrator-active");

                int window = s.BarkChatSuppressionMs;
                if (CompanionBusy(window, now)) return (false, -1, $"chat-suppressed ({window}ms)");

                bool willPreempt = rule.Class != BarkClass.Normal || rule.Priority >= PriorityBarkThreshold;
                if (!willPreempt && Ask(AvatarSpeaking)) return (false, -1, "speaking");

                if (!GlobalGapExemptTriggers.Contains(rule.Trigger))
                {
                    var sinceGlobal = (now - _globalLastFireUtc).TotalMilliseconds;
                    if (sinceGlobal < GlobalMinGapMs) return (false, -1, $"min-gap ({sinceGlobal:F0}/{GlobalMinGapMs}ms)");
                }

                if (rule.CooldownMs > 0 && _lastFiredUtc.TryGetValue(rule.Id, out var last))
                {
                    var sinceRule = (now - last).TotalMilliseconds;
                    if (sinceRule < rule.CooldownMs) return (false, -1, $"cooldown ({sinceRule:F0}/{rule.CooldownMs}ms)");
                }

                if (rule.Chance < 1.0 && _rng.NextDouble() >= rule.Chance) return (false, -1, $"chance ({rule.Chance:0.##})");
            }

            int idx = 0;
            if (pool.Count > 1)
            {
                if (rule.Repeatable)
                {
                    var usedKeys = _usedVariantKeys.TryGetValue(rule.Id, out var set) ? set : null;
                    idx = PickVariant(pool, rule.Id, usedKeys);
                    if (idx < 0)
                    {
                        var reseed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        if (_lastVariantKey.TryGetValue(rule.Id, out var lastKey)) reseed.Add(lastKey);
                        _usedVariantKeys[rule.Id] = reseed;
                        PersistVariantRotation(rule.Id);
                        idx = PickVariant(pool, rule.Id, reseed);
                        if (idx < 0) idx = 0;
                    }
                }
                else idx = _rng.Next(pool.Count);
            }
            return (true, idx, "OK");
        }

        private bool AlreadyFiredOnce(BarkRule rule)
        {
            if (_firedOnceSession.Contains(rule.Id)) return true;
            if (rule.Scope != BarkScope.Session) return Settings().IsBarkFired(LatchKey(rule));
            return false;
        }

        private string LatchKey(BarkRule rule)
        {
            if (rule.Scope != BarkScope.Tier) return rule.Id;
            string tier = "None";
            try { tier = TierName?.Invoke() ?? "None"; } catch { }
            return rule.Id + "@" + tier;
        }

        private bool CompanionBusy(int windowMs, DateTime now)
        {
            try { if (AvatarBusy?.Invoke(windowMs) == true) return true; } catch { }
            return windowMs > 0 && (now - _lastUserMessageUtc).TotalMilliseconds < windowMs;
        }

        // ===================== commit (WPF CommitFire :1793) =====================

        private void CommitFire(BarkRule rule, int variantIndex, List<BarkVariant> pool)
        {
            var now = Now;
            _lastFiredUtc[rule.Id] = now;
            _globalLastFireUtc = now;

            if (variantIndex >= 0 && variantIndex < pool.Count) RememberSpoken(pool[variantIndex].Audio);
            if (rule.Class == BarkClass.Safety) _safetyHoldUntilUtc = now.AddMilliseconds(SafetyHoldMs);

            if (!rule.Repeatable)
            {
                _firedOnceSession.Add(rule.Id);
                if (rule.Scope != BarkScope.Session && !DryRun) Settings().MarkBarkFired(LatchKey(rule));
            }

            if (variantIndex >= 0 && variantIndex < pool.Count)
            {
                var key = CompanionPhraseIds.BarkLineId(rule.Id, pool[variantIndex].Text, pool[variantIndex].Audio);
                if (!_usedVariantKeys.TryGetValue(rule.Id, out var set))
                    _usedVariantKeys[rule.Id] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                set.Add(key);
                _lastVariantKey[rule.Id] = key;
                PersistVariantRotation(rule.Id);
            }

            if (string.Equals(rule.Trigger, "Idle", StringComparison.OrdinalIgnoreCase))
            {
                _usedIdleRules.Add(rule.Id);
                _lastIdleRuleId = rule.Id;
                PersistIdleRotation();
            }
        }

        private int PickVariant(List<BarkVariant> pool, string ruleId, HashSet<string>? usedKeys)
        {
            List<int>? unused = null;
            for (int i = 0; i < pool.Count; i++)
                if (usedKeys == null || !usedKeys.Contains(CompanionPhraseIds.BarkLineId(ruleId, pool[i].Text, pool[i].Audio)))
                    (unused ??= new List<int>()).Add(i);
            if (unused == null || unused.Count == 0) return -1;

            List<int>? fresh = null;
            if (_recentlySpokenSet.Count > 0)
                foreach (var i in unused)
                    if (!_recentlySpokenSet.Contains(pool[i].Audio ?? string.Empty))
                        (fresh ??= new List<int>()).Add(i);

            var pick = fresh ?? unused;
            return pick[_rng.Next(pick.Count)];
        }

        private void RememberSpoken(string? audio)
        {
            if (string.IsNullOrEmpty(audio)) return;
            if (!_recentlySpokenSet.Add(audio)) return;
            _recentlySpoken.Enqueue(audio);
            while (_recentlySpoken.Count > RecentlySpokenMemory)
                _recentlySpokenSet.Remove(_recentlySpoken.Dequeue());
        }

        private void PersistVariantRotation(string ruleId)
        {
            if (DryRun) return;
            Settings().BarkVariantRotation[ruleId] = _usedVariantKeys.TryGetValue(ruleId, out var set)
                ? new List<string>(set) : new List<string>();
            SaveSettings();
        }

        private void PersistIdleRotation()
        {
            if (DryRun) return;
            Settings().BarkIdleRotation = new List<string>(_usedIdleRules);
            SaveSettings();
        }

        // ===================== speak helpers (WPF :2000) =====================

        /// <summary>Substitute {0} (focused app, "that" when unknown) and {key} tokens from the context.</summary>
        public static string ApplySubstitutions(string text, BarkContext ctx, string? focusedApp = null)
        {
            if (string.IsNullOrEmpty(text)) return text;
            if (text.Contains("{0}"))
                text = text.Replace("{0}", string.IsNullOrWhiteSpace(focusedApp) ? "that" : focusedApp);
            foreach (var kvp in ctx.Values)
            {
                var token = "{" + kvp.Key + "}";
                if (text.Contains(token)) text = text.Replace(token, kvp.Value?.ToString() ?? "");
            }
            return text;
        }

        /// <summary>WPF TryGetSettingNumber: a setting's value as a double (bool 0/1, enum int); false otherwise.</summary>
        public static bool TryGetSettingNumber(string name, out double value)
        {
            value = 0;
            object? raw;
            try { raw = typeof(ConditioningControlPanel.Models.AppSettings).GetProperty(name)?.GetValue(CoreSettings.Current); }
            catch { return false; }
            switch (raw)
            {
                case bool b: value = b ? 1 : 0; return true;
                case int i: value = i; return true;
                case long l: value = l; return true;
                case double d: value = d; return true;
                case float f: value = f; return true;
                case Enum en: value = Convert.ToDouble(en); return true;
                default: return false;
            }
        }
    }
}
