using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Haptics;
using ConditioningControlPanel.Services.Haptics.Core;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.GoonGame
{
    /// <summary>
    /// The Goon page's toy cues, played on whatever the haptics service has connected (Lovense or
    /// Intiface, the service does not care which). ccp-bugs #1429: the host used to answer
    /// <c>caps.haptics = false</c> to every page and drop <c>toy-pattern</c>, so the lobby read
    /// "No toy connected" with a toy connected.
    ///
    /// The page is untrusted input. A cue is clamped to the cap it carries (the consent sheet's
    /// agreed toy cap, 0.7 when absent), the player's own master intensity and cap still apply
    /// inside the mixer, the newest cue replaces the one in play, and a stop is never delayed.
    /// Nothing plays while the window is closed, haptics are off or no device is connected.
    /// </summary>
    internal static class GoonToyDirector
    {
        public const double DefaultCap = 0.7;
        /// <summary>A sustained bed (durationMs 0) is one long segment; the page re-sends it.</summary>
        public const int SustainMs = 60_000, MinMs = 200, MaxMs = 60_000;
        /// <summary>Two cues closer than this are folded into the later one.</summary>
        public const int MinGapMs = 150;

        public readonly record struct Cue(double Level, int Ms, VibrationMode Mode);

        /// <summary>What the page is told in <c>caps.haptics</c> and <c>haptics-state</c>.</summary>
        public static bool Ready(bool hapticsEnabled, bool deviceConnected) => hapticsEnabled && deviceConnected;

        public static bool ReadyNow
            => Ready(App.Settings?.Current?.Haptics is { Enabled: true }, App.Haptics is { IsConnected: true });

        public static VibrationMode ModeFor(string? pattern) => (pattern ?? "").Trim().ToLowerInvariant() switch
        {
            "pulse" => VibrationMode.Pulse,
            "wave" => VibrationMode.Wave,
            "fireworks" => VibrationMode.Heartbeat,
            "earthquake" => VibrationMode.Earthquake,
            _ => VibrationMode.Constant,
        };

        /// <summary>A rendered pattern has its own ceiling (40 taps, 20 beats); longer cues repeat it.</summary>
        public static int SegmentMs(VibrationMode mode) => mode switch
        {
            VibrationMode.Pulse => 3_000,
            VibrationMode.Heartbeat => 8_000,
            VibrationMode.Wave => 4_000,
            VibrationMode.Earthquake => 5_000,
            _ => MaxMs,
        };

        public static int PriorityFor(double level) => level >= 0.7 ? 3 : level >= 0.4 ? 2 : 1;

        /// <summary>A <c>toy-pattern</c> frame as a playable cue, or null when it asks for nothing.</summary>
        public static Cue? Parse(JObject? o)
        {
            if (o == null) return null;
            var cap = o["cap"] == null || o["cap"]!.Type == JTokenType.Null ? DefaultCap : Unit(o["cap"]);
            var level = Math.Min(Unit(o["intensity"]), cap);
            if (level <= 0) return null;
            var ms = (int)Math.Clamp(Number(o["durationMs"]), 0, MaxMs);
            ms = ms <= 0 ? SustainMs : Math.Max(MinMs, ms);
            string? pattern = o["pattern"]?.Type == JTokenType.String ? (string?)o["pattern"] : null;
            return new Cue(level, ms, ModeFor(pattern));
        }

        private static double Number(JToken? t)
        {
            if (t == null || (t.Type != JTokenType.Float && t.Type != JTokenType.Integer)) return 0;
            var v = (double)t;
            return double.IsNaN(v) || double.IsInfinity(v) ? 0 : v;
        }

        private static double Unit(JToken? t) => Math.Clamp(Number(t), 0, 1);

        // ------------------------------------------------------------------ playback

        private static readonly object Gate = new();
        private static CancellationTokenSource? _playing;
        private static Cue? _pending;
        private static bool _pumping;
        private static bool _open;
        private static long _lastStart = long.MinValue;

        /// <summary>The Goon window is up: cues may play.</summary>
        public static void Open() { lock (Gate) _open = true; }

        /// <summary>The Goon window is gone (closed, panic, recovery): stop and refuse until reopened.</summary>
        public static void Close()
        {
            lock (Gate) _open = false;
            Stop();
        }

        public static void OnPattern(JObject o)
        {
            var cue = Parse(o);
            if (cue == null) { Stop(); return; }
            if (!ReadyNow) return;
            int wait;
            lock (Gate)
            {
                if (!_open) return;
                _pending = cue;
                if (_pumping) return;
                _pumping = true;
                var since = _lastStart == long.MinValue ? MinGapMs : Environment.TickCount64 - _lastStart;
                wait = (int)Math.Clamp(MinGapMs - since, 0, MinGapMs);
            }
            _ = PumpAsync(wait);
        }

        private static async Task PumpAsync(int waitMs)
        {
            try { if (waitMs > 0) await Task.Delay(waitMs).ConfigureAwait(false); } catch { }
            Cue cue;
            CancellationTokenSource cts;
            lock (Gate)
            {
                _pumping = false;
                if (_pending == null || !_open) { _pending = null; return; }
                cue = _pending.Value;
                _pending = null;
                try { _playing?.Cancel(); } catch { }
                cts = _playing = new CancellationTokenSource();
                _lastStart = Environment.TickCount64;
            }
            var haptics = App.Haptics;
            try
            {
                if (haptics == null) return;
                App.Logger?.Debug("GoonToys: {Mode} {Pct}% {Ms}ms", cue.Mode, (int)(cue.Level * 100), cue.Ms);
                var left = cue.Ms;
                while (left > 0 && !cts.IsCancellationRequested && ReadyNow)
                {
                    var seg = Math.Min(left, SegmentMs(cue.Mode));
                    var sw = Stopwatch.StartNew();
                    await haptics.PlayPatternAsync(cue.Level, seg, cue.Mode, PriorityFor(cue.Level), ToyRole.All, cts.Token)
                        .ConfigureAwait(false);
                    // Cut short from outside (panic stop): never start the toy again on our own.
                    if (sw.ElapsedMilliseconds < seg - 300) break;
                    left -= seg;
                }
            }
            catch { /* a toy that dropped mid-pattern is not the match's problem */ }
            finally
            {
                lock (Gate) { if (_playing == cts) _playing = null; }
                cts.Dispose();
            }
        }

        /// <summary>Stop OUR cue only, at once. Whatever else drives the toy keeps going.</summary>
        public static void Stop()
        {
            CancellationTokenSource? cts;
            lock (Gate) { _pending = null; cts = _playing; _playing = null; }
            try { cts?.Cancel(); } catch { }
        }
    }
}
