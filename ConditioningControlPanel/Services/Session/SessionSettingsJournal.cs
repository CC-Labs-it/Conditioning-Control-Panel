using System;
using System.Collections.Generic;
using System.IO;
using ConditioningControlPanel.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// The user's own values from before a session, kept on disk while it runs (ccp-bugs #1488 and
    /// perly, 2026-10-21: "Audio Ducking turns itself on, flash opacity and pink filter get stronger,
    /// Hydra mode turns itself on").
    ///
    /// ApplySessionSettings writes the session's values straight into AppSettings, which auto-saves,
    /// and RestoreSettings hands the user's back only on a clean stop. A crash, a kill or a hang the
    /// user ends from Task Manager left the session's values in settings.json for good. The engine
    /// writes this file right after its snapshot and deletes it after RestoreSettings; a file still
    /// there at the next launch is applied before anything reads the settings.
    ///
    /// <see cref="Fields"/> is exactly what RestoreSettings restores (SessionSettingsJournalTests).
    /// The three phrase pools come back only when the same mod is still active.
    /// </summary>
    internal static class SessionSettingsJournal
    {
        public const string FileName = "session_restore.json";

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

        private static string PathIn(string dir) => Path.Combine(dir, FileName);

        /// <summary>The journal for this snapshot, as JSON. Pure.</summary>
        public static string Build(AppSettings saved, string? modId,
            IDictionary<string, bool>? subliminalPool, IDictionary<string, bool>? bouncingTextPool, IDictionary<string, bool>? lockCardPool)
        {
            var fields = new JObject();
            foreach (var name in Fields)
            {
                var p = typeof(AppSettings).GetProperty(name);
                if (p != null) fields[name] = JToken.FromObject(p.GetValue(saved) ?? JValue.CreateNull());
            }
            var o = new JObject { ["mod"] = modId, ["fields"] = fields };
            if (subliminalPool != null) o["subliminal_pool"] = JObject.FromObject(subliminalPool);
            if (bouncingTextPool != null) o["bouncing_text_pool"] = JObject.FromObject(bouncingTextPool);
            if (lockCardPool != null) o["lock_card_pool"] = JObject.FromObject(lockCardPool);
            return o.ToString(Formatting.None);
        }

        /// <summary>Puts the journal's values back into <paramref name="current"/>. Returns how many
        /// fields it wrote. Unknown or unreadable fields are skipped. Pure.</summary>
        public static int Apply(string json, AppSettings current, string? activeModId)
        {
            var o = JObject.Parse(json);
            var applied = 0;
            if (o["fields"] is JObject fields)
            {
                foreach (var name in Fields)
                {
                    if (fields[name] is not { } token) continue;
                    var p = typeof(AppSettings).GetProperty(name);
                    if (p == null || !p.CanWrite) continue;
                    try { p.SetValue(current, token.ToObject(p.PropertyType)); applied++; }
                    catch { /* one bad field never costs the others */ }
                }
            }
            if (string.Equals((string?)o["mod"], activeModId, StringComparison.Ordinal))
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

        public static void Write(string dir, string json)
        {
            try
            {
                var path = PathIn(dir);
                var tmp = path + ".tmp";
                File.WriteAllText(tmp, json);
                File.Move(tmp, path, overwrite: true);
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "[Session] Could not write the settings journal"); }
        }

        public static void Clear(string dir)
        {
            try { File.Delete(PathIn(dir)); }
            catch (Exception ex) { App.Logger?.Debug("[Session] Could not delete the settings journal: {E}", ex.Message); }
        }

        /// <summary>Launch: a journal left behind means the last session never stopped cleanly.
        /// Put the user's own values back and delete it. True when anything was healed.</summary>
        public static bool HealOnLaunch(string dir, AppSettings? current)
        {
            var path = PathIn(dir);
            if (current == null || !File.Exists(path)) return false;
            try
            {
                var n = Apply(File.ReadAllText(path), current, current.ActiveModId);
                App.Logger?.Information("[Session] The last session did not stop cleanly; restored {Count} of the user's settings", n);
                return n > 0;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "[Session] The settings journal could not be read; left as is");
                return false;
            }
            finally { Clear(dir); }
        }
    }
}
