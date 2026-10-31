using System;
using System.Threading;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Haptics.Core;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Haptics
{
    /// <summary>
    /// Haptics for Racing Thoughts 3D (glassroad CONTRACT.md section 7, "Toys"). The page has already decided what a
    /// moment feels like (fx/toyPlan.js) and sends flat pulses <c>toy {level, ms}</c> and a steady <c>toy-bed {level}</c>;
    /// this director only decides whether the toy takes them. It does nothing when haptics are off, no device is connected
    /// or the Racing Thoughts row is off, and it rate limits again because the page is untrusted input.
    ///
    /// The bed is <see cref="HapticLayer.Race"/> with an auto-zero, so a page that dies cannot pin a toy. Pulses are mixer
    /// transients that ride over it, and the player's master intensity and cap apply inside the mixer. The row's scale
    /// starts at <see cref="DefaultScale"/> (owner 2026-10-31). Panic stops the mixer and closes the race window, which
    /// runs <see cref="Stop"/>, so the page has no say in stopping.
    /// </summary>
    internal static class RaceHapticDirector
    {
        public const int MinMs = 100, MaxMs = 4000, BedAutoZeroMs = 1500;
        public const double DefaultScale = 0.6;

        public readonly record struct Pulse(double Level, int Ms);

        /// <summary>A pulse off the wire: level 0..1 and a clamped length, or null when either is not a number.</summary>
        public static Pulse? ParsePulse(JObject? o)
        {
            if (Number(o?["level"]) is not { } level || Number(o?["ms"]) is not { } ms || level <= 0) return null;
            return new Pulse(Math.Min(level, 1), (int)Math.Clamp(ms, MinMs, MaxMs));
        }

        /// <summary>A bed level off the wire, 0..1, or null when it is not a number.</summary>
        public static double? ParseBed(JObject? o)
            => Number(o?["level"]) is { } level ? Math.Clamp(level, 0, 1) : null;

        private static double? Number(JToken? t)
        {
            if (t is not { Type: JTokenType.Float or JTokenType.Integer }) return null;
            var v = (double)t;
            return double.IsFinite(v) ? v : null;
        }

        /// <summary>Buttplug commands carry about a second of latency and a short pulse arrives as mush: stretch it.</summary>
        public static int DurationFor(int ms, bool buttplug) => buttplug ? Math.Min(MaxMs, ms * 2) : ms;

        private static readonly object Gate = new();
        private static readonly BackRoomHapticDirector.Limiter Limit = new();
        private static CancellationTokenSource? _playing;

        private static bool Ready
            => App.Settings?.Current?.Haptics is { Enabled: true } && App.Haptics is { IsConnected: true };

        /// <summary>The <c>toy</c>, <c>toy-bed</c> and <c>toy-stop</c> frames of the race page.</summary>
        public static void OnPageMessage(JObject o)
        {
            switch ((string?)o["type"])
            {
                case "toy": if (ParsePulse(o) is { } pulse) OnPulse(pulse); break;
                case "toy-bed": if (ParseBed(o) is { } level) OnBed(level); break;
                case "toy-stop": Stop(); break;
            }
        }

        private static void OnBed(double level)
        {
            if (!Ready) return;
            App.Haptics?.SetLayer(HapticLayer.Race, level, BedAutoZeroMs);
        }

        private static void OnPulse(Pulse p)
        {
            var haptics = App.Haptics;
            var rule = App.Settings?.Current?.Haptics?.V2.Rule(HapticLayer.Race);
            if (haptics == null || rule is not { Enabled: true } || !Ready) return;

            CancellationTokenSource cts;
            lock (Gate)
            {
                if (!Limit.Accept(Environment.TickCount64)) return;
                try { _playing?.Cancel(); } catch { }
                cts = _playing = new CancellationTokenSource();
            }
            var level = p.Level * Math.Clamp(rule.Intensity, 0, 1);
            _ = PlayAsync(haptics, level, DurationFor(p.Ms, haptics.IsButtplugProvider), rule.Target, cts);
        }

        private static async System.Threading.Tasks.Task PlayAsync(HapticService haptics, double level, int ms, ToyRole target, CancellationTokenSource cts)
        {
            try { await haptics.PlayPatternAsync(level, ms, VibrationMode.Constant, BackRoomHapticDirector.PriorityFor(level), target, cts.Token); }
            catch { /* a toy that dropped mid-pulse is not the race's problem */ }
            finally
            {
                lock (Gate) { if (_playing == cts) _playing = null; }
                cts.Dispose();
            }
        }

        /// <summary>Stop OUR bed and pulse only (pause, a hidden page, window close). Whatever else drives the toy keeps going.</summary>
        public static void Stop()
        {
            CancellationTokenSource? cts;
            lock (Gate) { cts = _playing; _playing = null; }
            try { cts?.Cancel(); } catch { }
            try { App.Haptics?.SetLayer(HapticLayer.Race, 0); } catch { }
        }
    }
}
