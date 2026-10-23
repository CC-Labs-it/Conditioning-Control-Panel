using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// The user's own values from before a session or an intensity ramp, kept on disk while it runs
    /// (ccp-bugs #1488 and perly, 2026-10-21: "Audio Ducking turns itself on, flash opacity and pink
    /// filter get stronger, subliminal volume never adjusts back, Hydra mode turns itself on").
    /// Main 8a89eaeb0 + 36370021a, moved to Core so both heads use it.
    ///
    /// A session (WPF SessionEngine.ApplySessionSettings, Core SessionRunner.Apply) and the engine's
    /// intensity ramp (WPF MainWindow RampTimer_Tick) write their values straight into AppSettings,
    /// which auto-saves, and hand the user's back only on a clean stop. A crash, a kill or a hang the
    /// user ends from Task Manager left them in settings.json for good. Each writes its journal right
    /// after its snapshot and deletes it after its own restore; a journal still there at the next
    /// launch is applied before anything reads the settings, newest first, so two of them unwind in
    /// the right order.
    ///
    /// <see cref="Fields"/> is exactly what a session restores (WPF RestoreSettings and Core
    /// SessionSettingsSnapshot) and <see cref="RampFields"/> exactly what the ramp stores
    /// (SessionSettingsJournalTests). The three phrase pools come back only when the same mod is
    /// still active.
    /// </summary>
    public static class SessionSettingsJournal
    {
        public const string FileName = "session_restore.json";
        public const string RampFileName = "ramp_restore.json";

        public static readonly IReadOnlyList<string> Fields = new[]
        {
            nameof(AppSettings.FlashEnabled), nameof(AppSettings.FlashFrequency), nameof(AppSettings.FlashOpacity),
            nameof(AppSettings.FlashClickable), nameof(AppSettings.CorruptionMode), nameof(AppSettings.FlashAudioEnabled),
            nameof(AppSettings.ImageScale), nameof(AppSettings.SimultaneousImages),
            nameof(AppSettings.SubliminalEnabled), nameof(AppSettings.SubliminalFrequency), nameof(AppSettings.SubliminalOpacity),
            nameof(AppSettings.SubliminalDuration),
            nameof(AppSettings.SubAudioEnabled), nameof(AppSettings.SubAudioVolume),
            nameof(AppSettings.AudioDuckingEnabled), nameof(AppSettings.DuckingLevel),
            nameof(AppSettings.PinkFilterEnabled), nameof(AppSettings.PinkFilterOpacity),
            nameof(AppSettings.SpiralEnabled), nameof(AppSettings.SpiralOpacity),
            nameof(AppSettings.BrainDrainEnabled), nameof(AppSettings.BrainDrainIntensity),
            nameof(AppSettings.BubblesEnabled), nameof(AppSettings.BubblesFrequency), nameof(AppSettings.BubblesClickable),
            nameof(AppSettings.BouncingTextEnabled), nameof(AppSettings.BouncingTextSpeed),
            nameof(AppSettings.BouncingTextSize), nameof(AppSettings.BouncingTextOpacity),
            nameof(AppSettings.MandatoryVideosEnabled), nameof(AppSettings.VideosPerHour),
            nameof(AppSettings.LockCardEnabled), nameof(AppSettings.LockCardFrequency),
            nameof(AppSettings.PopQuizFrequency), nameof(AppSettings.BubbleCountEnabled), nameof(AppSettings.BubbleCountFrequency),
        };

        public static readonly IReadOnlyList<string> RampFields = new[]
        {
            nameof(AppSettings.FlashOpacity), nameof(AppSettings.SpiralOpacity), nameof(AppSettings.PinkFilterOpacity),
            nameof(AppSettings.MasterVolume), nameof(AppSettings.SubAudioVolume), nameof(AppSettings.BrainDrainBlurStrength),
        };

        /// <summary>The session journal for this snapshot, as JSON. Pure.</summary>
        public static string Build(AppSettings saved, string? modId,
            IDictionary<string, bool>? subliminalPool, IDictionary<string, bool>? bouncingTextPool, IDictionary<string, bool>? lockCardPool,
            long? atTicks = null)
        {
            var fields = new JObject();
            foreach (var name in Fields)
            {
                var p = typeof(AppSettings).GetProperty(name);
                if (p != null) fields[name] = JToken.FromObject(p.GetValue(saved) ?? JValue.CreateNull());
            }
            var o = new JObject { ["at"] = atTicks ?? DateTime.UtcNow.Ticks, ["mod"] = modId, ["fields"] = fields };
            if (subliminalPool != null) o["subliminal_pool"] = JObject.FromObject(subliminalPool);
            if (bouncingTextPool != null) o["bouncing_text_pool"] = JObject.FromObject(bouncingTextPool);
            if (lockCardPool != null) o["lock_card_pool"] = JObject.FromObject(lockCardPool);
            return o.ToString(Formatting.None);
        }

        /// <summary>The ramp journal from the ramp's base values (keyed by setting name). Pure.</summary>
        public static string BuildRamp(IReadOnlyDictionary<string, double> bases, long? atTicks = null)
        {
            var fields = new JObject();
            foreach (var name in RampFields)
                if (bases.TryGetValue(name, out var v)) fields[name] = (int)v;
            return new JObject { ["at"] = atTicks ?? DateTime.UtcNow.Ticks, ["fields"] = fields }.ToString(Formatting.None);
        }

        /// <summary>Puts a session journal's values back into <paramref name="current"/>. Returns how
        /// many fields it wrote. Unknown or unreadable fields are skipped. Pure.</summary>
        public static int Apply(string json, AppSettings current, string? activeModId)
            => Apply(JObject.Parse(json), current, activeModId, Fields);

        /// <summary>Puts a ramp journal's base values back. Pure.</summary>
        public static int ApplyRamp(string json, AppSettings current)
            => Apply(JObject.Parse(json), current, null, RampFields);

        private static int Apply(JObject o, AppSettings current, string? activeModId, IReadOnlyList<string> allowed)
        {
            var applied = 0;
            if (o["fields"] is JObject fields)
            {
                foreach (var name in allowed)
                {
                    if (fields[name] is not { } token) continue;
                    var p = typeof(AppSettings).GetProperty(name);
                    if (p == null || !p.CanWrite) continue;
                    try { p.SetValue(current, token.ToObject(p.PropertyType)); applied++; }
                    catch { /* one bad field never costs the others */ }
                }
            }
            if (o["mod"] != null && string.Equals((string?)o["mod"], activeModId, StringComparison.Ordinal))
            {
                Refill(current.SubliminalPool, o["subliminal_pool"]);
                Refill(current.BouncingTextPool, o["bouncing_text_pool"]);
                Refill(current.LockCardPhrases, o["lock_card_pool"]);
            }
            return applied;
        }

        private static void Refill(Dictionary<string, bool>? pool, JToken? saved)
        {
            if (pool == null || saved is not JObject o) return;
            var map = o.ToObject<Dictionary<string, bool>>();
            if (map == null) return;
            pool.Clear();
            foreach (var kv in map) pool[kv.Key] = kv.Value;
        }

        public static void Write(string dir, string json) => WriteFile(dir, FileName, json);
        public static void Clear(string dir) => ClearFile(dir, FileName);
        public static void WriteRamp(string dir, string json) => WriteFile(dir, RampFileName, json);
        public static void ClearRamp(string dir) => ClearFile(dir, RampFileName);

        private static void WriteFile(string dir, string name, string json)
        {
            try
            {
                var path = Path.Combine(dir, name);
                var tmp = path + ".tmp";
                File.WriteAllText(tmp, json);
                File.Move(tmp, path, overwrite: true);
            }
            catch (Exception ex) { Log.Warning(ex, "[Session] Could not write {Journal}", name); }
        }

        private static void ClearFile(string dir, string name)
        {
            try { File.Delete(Path.Combine(dir, name)); }
            catch (Exception ex) { Log.Debug("[Session] Could not delete {Journal}: {E}", name, ex.Message); }
        }

        /// <summary>Launch: a journal left behind means a session or a ramp never stopped cleanly.
        /// Put the user's own values back, newest journal first, and delete them. True when
        /// anything was healed.</summary>
        public static bool HealOnLaunch(string dir, AppSettings? current)
        {
            if (current == null) return false;
            var found = new List<(string Name, JObject Json, long At)>();
            foreach (var name in new[] { FileName, RampFileName })
            {
                var path = Path.Combine(dir, name);
                if (!File.Exists(path)) continue;
                try
                {
                    var o = JObject.Parse(File.ReadAllText(path));
                    found.Add((name, o, (long?)o["at"] ?? 0));
                }
                catch (Exception ex) { Log.Warning(ex, "[Session] {Journal} could not be read; left as is", name); }
                finally { ClearFile(dir, name); }
            }

            var healed = 0;
            foreach (var j in found.OrderByDescending(j => j.At))
            {
                try
                {
                    var n = j.Name == RampFileName
                        ? Apply(j.Json, current, null, RampFields)
                        : Apply(j.Json, current, current.ActiveModId, Fields);
                    healed += n;
                    Log.Information("[Session] {Journal}: the last run did not stop cleanly; restored {Count} of the user's settings", j.Name, n);
                }
                catch (Exception ex) { Log.Warning(ex, "[Session] {Journal} could not be applied", j.Name); }
            }
            return healed > 0;
        }
    }
}
