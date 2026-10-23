using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Services.Companion;
using Serilog;

namespace ConditioningControlPanel.Services.Bark
{
    /// <summary>A line for the mouth: text, clip or null, preempting (WPF GigglePriority), mood, muted easter egg.</summary>
    public readonly record struct BarkSpeech(string Text, string? AudioPath, bool Priority, string? Mood, bool SilentEgg, bool Unprompted = false);

    /// <summary>
    /// The matcher, gate and rotation of WPF 7.1.5 <c>Services/Companion/BarkService.cs</c>, so every head
    /// runs the same rules with the same numbers: the priority walk (Raise :1096), conditions with the
    /// _gte/_lte/_gt/_lt/_eq suffixes (:1379), the gate (EvaluateGate :1645: EMI mute, companion off,
    /// one-shot latches, safety hold, chat suppression, speaking, the 60 s floor and its exemptions,
    /// cooldown, chance), the no-repeat variant rotation persisted in AppSettings (:1793-1941) and the
    /// {0}/{key} substitutions (:2000). The head owns the event subscriptions and the mouth.
    /// ponytail: PickVoiceLine and the awareness arbiter land with their callers; the Chaos-narrator
    /// floor awaits that head-side producer. Subliminal whispers report their duration here.
    /// </summary>
    public sealed class BarkEngine
    {
        public const int GlobalMinGapMs = 60000;
        public const int PriorityBarkThreshold = 100;
        public const int SafetyHoldMs = 6000;
        public const int RecentlySpokenMemory = 8;
        public static readonly TimeSpan RapidModSwitchWindow = TimeSpan.FromSeconds(60);
        public static readonly TimeSpan RapidClickWindow = TimeSpan.FromSeconds(60);

        /// <summary>WPF GlobalGapExemptTriggers (:77): functional corrections skip the 60 s floor.</summary>
        public static readonly HashSet<string> GlobalGapExemptTriggers = new(StringComparer.OrdinalIgnoreCase)
        {
            "AttentionCheckFail", "PossessionEffect", "PossessionTripwire", "PossessionWarden", "PossessionRungChanged",
            "PossessionTimerRestarted", "PossessionRemember", "LockdownConscript", "EmergencyExitOpened", "EmergencyExitVerdict",
        };

        /// <summary>WPF IsPossessionAttribution + IsEmergencyExitAttribution (:107-122): text-only
        /// attributions that may cross a live whisper. Kept separate from the gap list, as in WPF.</summary>
        public static readonly HashSet<string> WhisperExemptTriggers = new(StringComparer.OrdinalIgnoreCase)
        {
            "PossessionEffect", "PossessionTripwire", "PossessionWarden", "PossessionRungChanged",
            "PossessionTimerRestarted", "PossessionRemember", "LockdownConscript", "EmergencyExitOpened", "EmergencyExitVerdict",
        };

        private readonly object _gate = new();
        private readonly Func<BarkRuleSet> _loadRules;
        private readonly Random _rng;
        private BarkRuleSet _rules = BarkRuleSet.Empty;
        private readonly Dictionary<string, DateTime> _lastFiredUtc = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _firedOnceSession = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, HashSet<string>> _usedVariantKeys = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _lastVariantKey = new(StringComparer.OrdinalIgnoreCase);
        private readonly Queue<string> _recentlySpoken = new();
        private readonly HashSet<string> _recentlySpokenSet = new(StringComparer.OrdinalIgnoreCase);
        private DateTime _globalLastFireUtc = DateTime.MinValue;
        private DateTime _safetyHoldUntilUtc = DateTime.MinValue;
        private DateTime _lastUserMessageUtc = DateTime.MinValue;
        private DateTime _whisperBusyUntilUtc = DateTime.MinValue;
        private readonly HashSet<string> _usedIdleRules = new(StringComparer.OrdinalIgnoreCase);
        private string? _lastIdleRuleId;
        private const double GatedIdleBias = 0.35;

        // WPF BarkState's rolling mod-switch and avatar-click windows, the counters a source on this head feeds.
        private readonly List<DateTime> _modSwitches = new(), _avatarClicks = new();
        public void RegisterModSwitch() => Register(_modSwitches, 64);
        public int ModSwitchesWithin(TimeSpan window) => CountWithin(_modSwitches, window);
        public void RegisterAvatarClick() => Register(_avatarClicks, 256);
        public int AvatarClicksWithin(TimeSpan window) => CountWithin(_avatarClicks, window);
        private void Register(List<DateTime> list, int cap) { lock (list) { list.Add(Now); if (list.Count > cap) list.RemoveRange(0, list.Count - cap); } }
        private int CountWithin(List<DateTime> list, TimeSpan window) { var cutoff = Now - window; lock (list) return list.Count(t => t >= cutoff); }

        /// <summary>The gap / cooldown / hold clock; tests step it (P08).</summary>
        public TimeProvider Clock { get; set; } = TimeProvider.System;
        private DateTime Now => Clock.GetUtcNow().UtcDateTime;

        // Head seams; unseeded = WPF's answer for a missing service. No mouth = nothing said, nothing spent.
        public Action<BarkSpeech>? Speak;
        public Func<bool>? IsChaosNarratorPlaying;
        public Func<string?>? CurrentTier;
        /// <summary>Evaluate and retain session timing/rotation, but never speak or mutate settings.</summary>
        public bool DryRun { get; set; }
        public Func<bool>? IsSpeaking;                 // App.AvatarWindow.IsSpeaking
        public Func<int, bool>? IsCompanionBusy;       // App.AvatarWindow.IsCompanionBusy(windowMs)
        public Func<bool>? EmiDeskAvatarMuted;         // App.EmiDesk.AvatarMuted
        public Func<string?>? FocusedAppName;          // WindowAwareness service ?? detected name, for {0}
        /// <summary>video_playing, webcam_running, achievements_all_unlocked, all_skills_unlocked (null = false).</summary>
        public Func<string, object?>? LiveField;
        /// <summary>WPF ResolveBarkAudio (:1625): the active mod's ladder.</summary>
        public Func<string, string?> ResolveAudio = file =>
            ModCompanionContent.ResolveActive(CompanionChannel.BarkAudio, file) is { Found: true } pick ? pick.Path : null;

        /// <summary>WPF BarkSpoken (:143): a line reached the mouth, with its final text (the brain's echo).</summary>
        public event Action<BarkRule, string>? BarkSpoken;

        public BarkEngine(Func<BarkRuleSet>? loadRules = null, Random? rng = null)
        {
            _loadRules = loadRules ?? BarkRuleManifest.Load;
            _rng = rng ?? new Random();
        }

        public int RuleCount { get { lock (_gate) return _rules.Count; } }

        /// <summary>WPF Start (:190): the rules and the persisted no-repeat rotation.</summary>
        public void Start()
        {
            if (string.Equals(Environment.GetEnvironmentVariable("CCP_BARK_DRYRUN"), "1", StringComparison.Ordinal))
                DryRun = true;
            var fresh = SafeLoad();
            var s = CoreSettings.Current;
            lock (_gate)
            {
                _rules = fresh;
                _usedVariantKeys.Clear();
                foreach (var kv in s.BarkVariantRotation)
                    _usedVariantKeys[kv.Key] = new HashSet<string>(kv.Value ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
                _usedIdleRules.Clear();
                foreach (var id in s.BarkIdleRotation) _usedIdleRules.Add(id);
            }
            Log.Information("BarkEngine started - {Count} rules", fresh.Count);
        }

        /// <summary>WPF ReloadRules (:632): session one-shots and cooldowns re-arm (ids are shared across mods);
        /// rotation and lifetime latches stay.</summary>
        public void ReloadRules()
        {
            var fresh = SafeLoad();
            lock (_gate)
            {
                _rules = fresh;
                _firedOnceSession.Clear();
                _lastFiredUtc.Clear();
            }
            Log.Information("BarkEngine: reloaded {Count} rules (rotation preserved)", fresh.Count);
        }

        private BarkRuleSet SafeLoad()
        { try { return _loadRules() ?? BarkRuleSet.Empty; } catch (Exception ex) { Log.Warning(ex, "BarkEngine: rule load failed"); return BarkRuleSet.Empty; } }

        /// <summary>WPF AudioService.MarkWhisperAudio: clip duration plus 250 ms, extend-only.</summary>
        public void NoteWhisperAudio(TimeSpan duration)
        {
            if (duration <= TimeSpan.Zero) return;
            lock (_gate)
            {
                var until = Now + duration + TimeSpan.FromMilliseconds(250);
                if (until > _whisperBusyUntilUtc) _whisperBusyUntilUtc = until;
            }
        }

        public void NoteUserMessage() { lock (_gate) _lastUserMessageUtc = Now; }   // WPF :806, chat suppression

        /// <summary>WPF Raise (:1096): the first passing rule (priority order) wins; the gate decides under the
        /// lock, the mouth speaks after it. True when it spoke.</summary>
        public bool Raise(string trigger, Action<BarkContext>? fill = null, bool guaranteed = false)
        {
            try
            {
                IReadOnlyList<BarkRule> rules;
                lock (_gate) rules = _rules.ForTrigger(trigger);
                if (rules.Count == 0) return false;
                var ctx = new BarkContext(trigger);
                fill?.Invoke(ctx);

                BarkRule? winner;
                int idx;
                List<BarkVariant> pool;
                lock (_gate)
                {
                    winner = rules.FirstOrDefault(r => ConditionsPass(r, ctx));
                    if (winner == null) { Log.Debug("[BARK] trigger={Trigger} no rule matched conditions", trigger); return false; }
                    (pool, idx) = DecideLocked(winner, guaranteed);
                    if (idx < 0) return false;
                }
                return SpeakLine(winner, pool[idx], ctx);
            }
            catch (Exception ex) { Log.Warning(ex, "BarkEngine: Raise('{Trigger}') failed", trigger); return false; }
        }

        // WPF DecideLocked: commit the in-memory decision even in dry-run, but no persisted writes.
        private (List<BarkVariant> Pool, int Index) DecideLocked(BarkRule winner, bool guaranteed)
        {
            var pool = ResolvePool(winner);
            var (fire, idx, reason) = EvaluateGate(winner, pool, guaranteed);
            if (fire)
                Log.Information("[BARK{Mode}] {Verb} trigger={Trigger} rule={Rule} variant#={Index}",
                    DryRun ? " dry-run" : "", DryRun ? "WOULD FIRE" : "FIRE", winner.Trigger, winner.Id, idx);
            else
                Log.Debug("[BARK{Mode}] blocked trigger={Trigger} rule={Rule} reason={Reason}",
                    DryRun ? " dry-run" : "", winner.Trigger, winner.Id, reason);
            if (!fire || (!DryRun && Speak == null)) return (pool, -1);
            CommitFire(winner, idx, pool);
            return (pool, DryRun ? -1 : idx);
        }

        /// <summary>WPF DispatchIdle: the tube owns cadence; idle rotates across eligible rules,
        /// biased 0.35 toward conditional rules. A blocked tick spends no rule.</summary>
        public bool DispatchIdle()
        {
            try
            {
                if (CoreSettings.Current.MasterVolume == 0 || Safe(IsSpeaking)) return false;
                var ctx = new BarkContext("Idle");
                BarkRule winner;
                List<BarkVariant> pool;
                int idx;
                lock (_gate)
                {
                    var eligible = _rules.ForTrigger("Idle").Where(r => ConditionsPass(r, ctx)).ToList();
                    if (eligible.Count == 0) return false;
                    var unused = eligible.Where(r => !_usedIdleRules.Contains(r.Id)).ToList();
                    if (unused.Count == 0)
                    {
                        _usedIdleRules.Clear();
                        if (_lastIdleRuleId != null) _usedIdleRules.Add(_lastIdleRuleId);
                        PersistIdleRotation();
                        unused = eligible.Where(r => !_usedIdleRules.Contains(r.Id)).ToList();
                        if (unused.Count == 0) unused = eligible;
                    }
                    var gated = unused.Where(r => r.Conditions is { Count: > 0 }).ToList();
                    var candidates = gated.Count > 0 && _rng.NextDouble() < GatedIdleBias ? gated : unused;
                    winner = candidates[_rng.Next(candidates.Count)];
                    (pool, idx) = DecideLocked(winner, guaranteed: false);
                    if (idx < 0) return false;
                }
                return SpeakLine(winner, pool[idx], ctx);
            }
            catch (Exception ex) { Log.Warning(ex, "BarkEngine: DispatchIdle failed"); return false; }
        }

        public bool ConditionsPass(BarkRule rule, BarkContext ctx) =>
            rule.Conditions == null || rule.Conditions.All(kv => ConditionPass(kv.Key, kv.Value, ctx));

        private bool ConditionPass(string key, object? expected, BarkContext ctx)
        {
            string field = key, op = "eq";
            var suffix = new[] { "_gte", "_lte", "_gt", "_lt", "_eq" }.FirstOrDefault(x => key.EndsWith(x, StringComparison.OrdinalIgnoreCase));
            if (suffix != null) (field, op) = (key[..^suffix.Length], suffix[1..]);
            var actual = ResolveField(field, ctx);
            if (actual == null) return false;
            if (op == "eq")
            {
                if (expected is bool eb && TryBool(actual, out var ab)) return ab == eb;
                if (TryDouble(expected, out var en) && TryDouble(actual, out var an)) return Math.Abs(an - en) < 0.0001;
                return string.Equals(actual.ToString(), expected?.ToString(), StringComparison.OrdinalIgnoreCase);
            }
            if (!TryDouble(actual, out var a) || !TryDouble(expected, out var e)) return false;
            return op switch { "gte" => a >= e, "lte" => a <= e, "gt" => a > e, "lt" => a < e, _ => false };
        }

        /// <summary>WPF ResolveField (:1417): well-known live reads first, else the per-fire context.</summary>
        private object? ResolveField(string field, BarkContext ctx)
        {
            var s = CoreSettings.Current;
            switch (field.ToLowerInvariant())
            {
                case "video_playing":
                case "webcam_running":
                case "achievements_all_unlocked":
                case "all_skills_unlocked":
                    try { return LiveField?.Invoke(field.ToLowerInvariant()) ?? false; } catch { return false; }
                case "session_running": return CoreSession.IsSessionRunning;
                case "mod_switches_60s": return ModSwitchesWithin(RapidModSwitchWindow);
                case "clicks_60s": return AvatarClicksWithin(RapidClickWindow);
                // ponytail: session_elapsed_sec, phase_*, blink_count, face_lost_sec, pauses/refocus, setup_idle_sec,
                // days_away and instant_relaunch are read only by triggers with no source here (session engine,
                // webcam, SessionSetupReady, SessionStarted); they land with those sources.
                case "master_volume": return (double)s.MasterVolume;
                case "mute": return s.MasterVolume == 0;
                case "player_level": return (double)s.PlayerLevel;
                case "total_sessions": return (double)s.TotalSessions;
                case "daily_quest_streak": return (double)s.DailyQuestStreak;
                case "current_streak": return (double)s.CurrentStreak;
                case "is_nye": { var now = DateTime.Now; return (now.Month == 12 && now.Day == 31) || (now.Month == 1 && now.Day == 1); }
                case "local_hour": return (double)DateTime.Now.Hour;
                case "sessions_7d": return (double)s.SessionsWithinDays(7);
                case "late_sessions_7d": return (double)s.LateSessionsWithinDays(7);
                case "sessions_today": return (double)s.SessionsToday();
                case "same_mod_run": return (double)s.SameModRun;
                case "is_late_hour": { int h = DateTime.Now.Hour; return h >= 23 || h < 4; }
                default: return ctx.Values.TryGetValue(field, out var v) ? v : null;
            }
        }

        // Same coercions as BarkContext.TryGetNumber / TryGetBool (WPF TryDouble / TryBool).
        private static bool TryDouble(object? raw, out double value) => new BarkContext("").Set("v", raw!).TryGetNumber("v", out value);
        private static bool TryBool(object? raw, out bool value) => new BarkContext("").Set("v", raw!).TryGetBool("v", out value);

        /// <summary>WPF ResolvePool (:1520): the inline pool minus Phrase-Manager-hidden lines, else pool_ref as text.</summary>
        private static List<BarkVariant> ResolvePool(BarkRule rule)
        {
            if (rule.VariantPool is { Count: > 0 })
            {
                var s = CoreSettings.Current;
                return rule.VariantPool.Where(v =>
                {
                    var id = BarkLineId(rule.Id, v);
                    return !s.DisabledPhraseIds.Contains(id) && !s.RemovedPhraseIds.Contains(id);
                }).ToList();
            }
            var phrases = string.IsNullOrWhiteSpace(rule.PoolRef) ? null : CoreMods.GetPhrases(rule.PoolRef!);
            return phrases is { Length: > 0 } ? phrases.Select(p => new BarkVariant(p)).ToList() : new List<BarkVariant>();
        }

        public static string BarkLineId(string ruleId, BarkVariant v) => CompanionPhraseIds.BarkLineId(ruleId, v.Text, v.Audio);

        /// <summary>WPF GetAllBarkLines (:1564): the inline lines, for the Phrase Manager (pool_ref ones are listed already).</summary>
        public IReadOnlyList<BarkLineInfo> GetAllBarkLines()
        {
            BarkRuleSet rules;
            lock (_gate) rules = _rules;
            return rules.AllRules.SelectMany(r => (r.VariantPool ?? new List<BarkVariant>()).Where(v => v.HasText).Select(v =>
                new BarkLineInfo(BarkLineId(r.Id, v), r.Id, r.Trigger, v.Text, v.Audio,
                    SafeResolveAudio(v.Audio) is { } path ? System.IO.Path.GetDirectoryName(path) : null))).ToList();
        }

        private string? SafeResolveAudio(string? file)
        {
            if (string.IsNullOrWhiteSpace(file)) return null;
            try { return ResolveAudio(file!); } catch { return null; }
        }

        private static bool Safe(Func<bool>? f) { try { return f?.Invoke() == true; } catch { return false; } }

        /// <summary>WPF EvaluateGate (:1645). Caller holds the lock.</summary>
        private (bool Fire, int Index, string Reason) EvaluateGate(BarkRule rule, List<BarkVariant> pool, bool guaranteed)
        {
            if (pool.Count == 0) return (false, -1, "empty-pool");
            bool isSafety = rule.Class == BarkClass.Safety;
            var s = CoreSettings.Current;

            if (!isSafety && Safe(EmiDeskAvatarMuted)) return (false, -1, "emi-desk-mute");
            if (!isSafety && !s.AvatarEnabled) return (false, -1, "companion-off");
            if (!isSafety && !rule.Repeatable && AlreadyFiredOnce(rule)) return (false, -1, $"already-fired ({rule.Scope})");

            if (!isSafety && !guaranteed)
            {
                var now = Now;
                if (now < _safetyHoldUntilUtc) return (false, -1, "safety-active");
                // WPF: only possession/emergency-exit text attributions may cross a whisper.
                if (now < _whisperBusyUntilUtc && !WhisperExemptTriggers.Contains(rule.Trigger)) return (false, -1, "whisper-active");
                if (Safe(IsChaosNarratorPlaying)) return (false, -1, "narrator-active");
                int window = s.BarkChatSuppressionMs;
                if (Safe(() => IsCompanionBusy?.Invoke(window) == true)
                    || (window > 0 && (now - _lastUserMessageUtc).TotalMilliseconds < window))
                    return (false, -1, $"chat-suppressed ({window}ms)");
                bool willPreempt = rule.Class != BarkClass.Normal || rule.Priority >= PriorityBarkThreshold;
                if (!willPreempt && Safe(IsSpeaking)) return (false, -1, "speaking");
                var sinceGlobal = (now - _globalLastFireUtc).TotalMilliseconds;
                if (!GlobalGapExemptTriggers.Contains(rule.Trigger) && sinceGlobal < GlobalMinGapMs)
                    return (false, -1, $"min-gap ({sinceGlobal:F0}/{GlobalMinGapMs}ms)");
                if (rule.CooldownMs > 0 && _lastFiredUtc.TryGetValue(rule.Id, out var last)
                    && (now - last).TotalMilliseconds < rule.CooldownMs)
                    return (false, -1, $"cooldown ({rule.CooldownMs}ms)");
                if (rule.Chance < 1.0 && _rng.NextDouble() >= rule.Chance) return (false, -1, $"chance ({rule.Chance:0.##})");
            }

            if (pool.Count == 1) return (true, 0, "OK");
            if (!rule.Repeatable) return (true, _rng.Next(pool.Count), "OK");   // one-shot: any line
            int idx = PickVariant(pool, rule.Id, _usedVariantKeys.TryGetValue(rule.Id, out var used) ? used : null);
            if (idx < 0)
            {
                // Pool exhausted: recycle, reseeded with the last line so it cannot repeat back-to-back.
                var reseed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (_lastVariantKey.TryGetValue(rule.Id, out var lastKey)) reseed.Add(lastKey);
                _usedVariantKeys[rule.Id] = reseed;
                PersistVariantRotation(rule.Id);
                idx = Math.Max(0, PickVariant(pool, rule.Id, reseed));
            }
            return (true, idx, "OK");
        }

        private bool AlreadyFiredOnce(BarkRule rule) =>
            _firedOnceSession.Contains(rule.Id)
            || (rule.Scope != BarkScope.Session && CoreSettings.Current.IsBarkFired(LatchKey(rule)));

        private string LatchKey(BarkRule rule) => rule.Scope == BarkScope.Tier ? rule.Id + "@" + (CurrentTier?.Invoke() ?? "None") : rule.Id;

        /// <summary>WPF CommitFire (:1793). Caller holds the lock.</summary>
        private void CommitFire(BarkRule rule, int idx, List<BarkVariant> pool)
        {
            var now = Now;
            _lastFiredUtc[rule.Id] = now;
            _globalLastFireUtc = now;
            RememberSpoken(pool[idx].Audio);
            if (rule.Class == BarkClass.Safety) _safetyHoldUntilUtc = now.AddMilliseconds(SafetyHoldMs);
            if (!rule.Repeatable)
            {
                _firedOnceSession.Add(rule.Id);
                if (rule.Scope != BarkScope.Session && !DryRun) CoreSettings.Current.MarkBarkFired(LatchKey(rule));
            }
            var key = BarkLineId(rule.Id, pool[idx]);
            if (!_usedVariantKeys.TryGetValue(rule.Id, out var set))
                _usedVariantKeys[rule.Id] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            set.Add(key);
            _lastVariantKey[rule.Id] = key;
            PersistVariantRotation(rule.Id);
            if (string.Equals(rule.Trigger, "Idle", StringComparison.OrdinalIgnoreCase))
            {
                _usedIdleRules.Add(rule.Id);
                _lastIdleRuleId = rule.Id;
                PersistIdleRotation();
            }
        }

        private void PersistIdleRotation()
        {
            if (DryRun) return;
            CoreSettings.Current.BarkIdleRotation = new List<string>(_usedIdleRules);
            CoreSettings.Save(suppressCloudBackup: true);
        }

        /// <summary>WPF PickVariant (:1867): an unused line, preferring a clip no rule spoke lately; -1 when all used.</summary>
        private int PickVariant(List<BarkVariant> pool, string ruleId, HashSet<string>? usedKeys)
        {
            var unused = Enumerable.Range(0, pool.Count)
                .Where(i => usedKeys == null || !usedKeys.Contains(BarkLineId(ruleId, pool[i]))).ToList();
            if (unused.Count == 0) return -1;
            var fresh = unused.Where(i => !_recentlySpokenSet.Contains(pool[i].Audio ?? string.Empty)).ToList();
            var pick = fresh.Count > 0 ? fresh : unused;
            return pick[_rng.Next(pick.Count)];
        }

        private void RememberSpoken(string? audio)
        {
            if (string.IsNullOrEmpty(audio) || !_recentlySpokenSet.Add(audio)) return;
            _recentlySpoken.Enqueue(audio);
            while (_recentlySpoken.Count > RecentlySpokenMemory) _recentlySpokenSet.Remove(_recentlySpoken.Dequeue());
        }

        private void PersistVariantRotation(string ruleId)
        {
            if (DryRun) return;
            CoreSettings.Current.BarkVariantRotation[ruleId] =
                _usedVariantKeys.TryGetValue(ruleId, out var set) ? new List<string>(set) : new List<string>();
            CoreSettings.Save(suppressCloudBackup: true);   // local-only, rides the next real change to cloud
        }

        /// <summary>WPF Speak (:1943): a muted easter egg shows text only; non-Normal or priority >= 100 preempts.</summary>
        private bool SpeakLine(BarkRule rule, BarkVariant variant, BarkContext ctx)
        {
            var mouth = Speak;
            var line = ApplySubstitutions(variant.Text, ctx);
            if (mouth == null || string.IsNullOrWhiteSpace(line)) return false;
            var mood = string.IsNullOrEmpty(rule.Mood) ? null : rule.Mood;
            if (rule.Class == BarkClass.EasterEgg && CoreSettings.Current.MasterVolume == 0)
                mouth(new BarkSpeech(line, null, false, mood, SilentEgg: true, Unprompted: string.Equals(rule.Trigger, "Idle", StringComparison.OrdinalIgnoreCase)));
            else
                mouth(new BarkSpeech(line, SafeResolveAudio(variant.Audio),
                    rule.Class != BarkClass.Normal || rule.Priority >= PriorityBarkThreshold, mood, SilentEgg: false, Unprompted: string.Equals(rule.Trigger, "Idle", StringComparison.OrdinalIgnoreCase)));
            try { BarkSpoken?.Invoke(rule, line); }
            catch (Exception ex) { Log.Debug("[BARK] BarkSpoken subscriber error: {Error}", ex.Message); }
            return true;
        }

        /// <summary>WPF ApplySubstitutions (:2000): {0} = the focused app ("that" when unknown), {key} = context.</summary>
        public string ApplySubstitutions(string text, BarkContext ctx)
        {
            if (string.IsNullOrEmpty(text)) return text;
            string? app = null;
            if (text.Contains("{0}")) try { app = FocusedAppName?.Invoke(); } catch { }
            text = text.Replace("{0}", string.IsNullOrWhiteSpace(app) ? "that" : app);
            foreach (var kvp in ctx.Values)
                text = text.Replace("{" + kvp.Key + "}", kvp.Value?.ToString() ?? "");
            return text;
        }
    }
}
