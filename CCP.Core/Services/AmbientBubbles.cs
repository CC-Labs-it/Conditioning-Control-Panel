using System;
using System.Collections.Generic;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// The ambient-pop daily XP bucket (WPF BubbleService, feat/xp-economy, #1019/#1026): 5 XP a
    /// pop, lucky rolls from the same bucket, capped at 300 per local calendar day with LAZY
    /// rollover on the {day key, paid} pair in AppSettings. No Save() here - a per-pop disk write
    /// would be the expensive half of a 5 XP grant; the counter rides out with the next save.
    /// </summary>
    public static class AmbientBubbleXp
    {
        public const int DailyXpCap = 300;

        private static string Today => DateTime.Now.ToString("yyyy-MM-dd");

        /// <summary>Paid today, 0..cap. Read-only: a stale day key reads as 0 without mutating settings.</summary>
        public static int PaidToday(AppSettings? s)
        {
            if (s == null || !string.Equals(s.AmbientBubbleXpDayKey, Today, StringComparison.Ordinal)) return 0;
            return Math.Clamp(s.AmbientBubbleXpPaidToday, 0, DailyXpCap);
        }

        public static int RemainingToday(AppSettings? s) => Math.Max(0, DailyXpCap - PaidToday(s));

        /// <summary>Draw <paramref name="earnedXp"/> from the bucket; returns what it can actually pay
        /// (a lucky roll near the ceiling pays the remainder, not nothing).</summary>
        public static int Take(AppSettings? s, int earnedXp)
        {
            if (s == null) return earnedXp;
            var today = Today;
            if (!string.Equals(s.AmbientBubbleXpDayKey, today, StringComparison.Ordinal))
            {
                s.AmbientBubbleXpDayKey = today;
                s.AmbientBubbleXpPaidToday = 0;
            }
            var paidSoFar = Math.Max(0, s.AmbientBubbleXpPaidToday);   // hand-edited negatives read as 0
            var remaining = DailyXpCap - paidSoFar;
            if (remaining <= 0) return 0;
            var pay = Math.Min(earnedXp, remaining);
            s.AmbientBubbleXpPaidToday = paidSoFar + pay;
            return pay;
        }
    }

    /// <summary>
    /// One plain ambient bubble (WPF <c>Bubble</c> with spec == null, FloatUp): the motion, wobble,
    /// breathe and pop-burst math of <c>Bubble.AnimateFrame</c>, one logical step (~30 ms) per
    /// <see cref="Step"/>. Positions are WPF's: DIPs of the bubble's own screen (screen px / scaling),
    /// top-left of the <see cref="Size"/> box. The head only draws and hit-tests.
    /// </summary>
    public sealed class AmbientBubble
    {
        public int Screen;
        public double X, Y, Size, Angle, Scale = 1, Fade = 1;
        public bool Clickable, Popping, Lucky;
        /// <summary>A Chaos run's bubble (WPF Bubble with a spec); null for a plain ambient one.</summary>
        public Chaos.EffectBubbleSpec? Spec;
        private double _startX, _top, _speed, _wobbleOffset, _timeAlive;
        private int _animType;

        // WPF BubbleService.cs:4430-4472. Coordinates are local to the unscaled bubble.
        public struct Sparkle
        {
            public double X, Y, VelX, VelY, Alpha, Size;
        }
        public Sparkle[] Sparkles { get; private set; } = Array.Empty<Sparkle>();

        internal void StartSparkles(Random random, bool boost)
        {
            if (!Lucky && !boost) return;
            Sparkles = new Sparkle[Lucky ? 16 : 8];
            for (var i = 0; i < Sparkles.Length; i++)
            {
                var angle = random.NextDouble() * Math.PI * 2;
                var speed = 2 + random.NextDouble() * 4;
                Sparkles[i] = new Sparkle
                {
                    X = Size / 2, Y = Size / 2,
                    VelX = Math.Cos(angle) * speed, VelY = Math.Sin(angle) * speed,
                    Alpha = 1, Size = Lucky ? 4 + random.NextDouble() * 4 : 3 + random.NextDouble() * 3
                };
            }
        }

        // ---- live chaos bubble (WPF Bubble _fuseRemainingMs / _isChanneling / TickChannel) ----
        /// <summary>WPF's fuse tick: 32 ms per logical step (BubbleService.cs:4149).</summary>
        public const double FuseStepMs = 32;
        public double FuseTotalMs, FuseRemainingMs;
        /// <summary>A hold-to-defuse channel is running: the bubble is pinned and the fuse paused.</summary>
        public bool Channeling { get; private set; }
        /// <summary>Set by the defuse that completed a channel (vs a sweep's snap).</summary>
        public bool DefusedViaChannel { get; private set; }
        /// <summary>Why a live bubble went off by hand: "click", "release" or "nofocus"; null for a fuse-out or escape.</summary>
        public string? BrokenReason { get; private set; }
        /// <summary>The bubble deflates (a snap) instead of bursting.</summary>
        public bool Deflating { get; private set; }
        private double _channelMs, _channelStartFuseSec;
        private DateTime _channelStartUtc;

        public enum Result { Alive, Missed, Done, Detonated, Defused }

        /// <summary>A live threat the player defuses by holding (WPF NeedsHoldDefuse for the plain lives).</summary>
        public bool IsLiveThreat => Spec is { IsLive: true, IsDarter: false, IsFreeze: false };
        public double FuseSecLeft => Math.Max(0, FuseRemainingMs) / 1000.0;
        /// <summary>WPF DefuseJudgeFuseSec: a held snap is judged at the channel's start (the fuse pauses).</summary>
        public double DefuseJudgeFuseSec => DefusedViaChannel ? _channelStartFuseSec : FuseSecLeft;
        public double FuseFraction => FuseTotalMs > 0 ? Math.Clamp(FuseRemainingMs / FuseTotalMs, 0, 1) : 0;
        /// <summary>WPF _channelScale: the bubble shrinks under the hold.</summary>
        public double ChannelScale => Channeling
            ? 1.0 - (1.0 - Chaos.ChaosTuning.CHANNEL_MIN_SCALE) * Math.Clamp(_channelMs / Chaos.ChaosTuning.DEFUSE_HOLD_MS, 0, 1)
            : 1.0;

        /// <summary>WPF OnPlayerPress on a live bubble: start the channel, or trigger it in the grip when
        /// the focus cannot pay ("nofocus"). Returns what the field reports.</summary>
        public Result Press(bool canChannel, DateTime nowUtc)
        {
            if (Popping || Channeling || !IsLiveThreat) return Result.Alive;
            if (!canChannel) return GoOff("nofocus");
            Channeling = true;
            _channelMs = 0;
            _channelStartUtc = nowUtc;
            _channelStartFuseSec = FuseSecLeft;
            return Result.Alive;
        }

        /// <summary>A sweep snaps it (WPF Pop() on a live bubble, e.g. DefuseAllLive): full pay, no channel.</summary>
        public bool Snap()
        {
            if (Popping || !IsLiveThreat) return false;
            Channeling = false;
            Popping = Deflating = true;
            return true;
        }

        /// <summary>WPF manual pause: the hold quietly cancels (no detonation, no completion).</summary>
        public void CancelChannel() { Channeling = false; _channelMs = 0; }

        private Result GoOff(string? reason)
        {
            Channeling = false;
            BrokenReason = reason;
            Popping = true;
            return Result.Detonated;
        }

        /// <summary>WPF TickChannel: the hold must stay pressed and on the bubble for DEFUSE_HOLD_MS;
        /// letting go under CLICK_THRESHOLD_MS reads as a click, later as a release; both trigger.
        /// The hold is wall time since the press (WPF :4531), not steps.</summary>
        private Result TickChannel(bool held, double cursorX, double cursorY, DateTime nowUtc)
        {
            _channelMs = Math.Max(0, (nowUtc - _channelStartUtc).TotalMilliseconds);
            if (!held || !Contains(cursorX, cursorY))
                return GoOff(!held && _channelMs < Chaos.ChaosTuning.CLICK_THRESHOLD_MS ? "click" : "release");
            if (_channelMs < Chaos.ChaosTuning.DEFUSE_HOLD_MS) return Result.Alive;
            Channeling = false;
            DefusedViaChannel = true;
            Popping = Deflating = true;
            return Result.Defused;
        }

        /// <summary>WPF Bubble ctor for a plain ambient bubble. <paramref name="area"/> is the screen's
        /// working area in px, <paramref name="scaling"/> its DPI scale.</summary>
        public static AmbientBubble Spawn(Random r, int screen, double areaX, double areaY, double areaW, double areaH,
            double scaling, AppSettings s, double? modScale, bool clickable)
        {
            var b = new AmbientBubble { Screen = screen, Clickable = clickable };
            b.Size = BubbleSizing.Scale(r.Next(BubbleSizing.BaseMinDip, BubbleSizing.BaseMaxDip), s.BubblesSize, modScale);
            b._speed = 1.0 + r.NextDouble();   // 1.0 to 2.0 DIP/step
            var boost = s.BubbleSpeedBoost;      // dashboard speed slider, up to +500%
            if (boost > 0) b._speed *= 1.0 + Math.Clamp(boost, 0, 500) / 100.0;
            b._animType = r.Next(4);
            b._wobbleOffset = r.NextDouble() * 100;
            b.Angle = r.Next(360);
            b._top = areaY / scaling - b.Size - 50;
            b.X = b._startX = (areaX + r.Next(50, (int)Math.Max(100, areaW - b.Size - 50))) / scaling;
            b.Y = (areaY + areaH) / scaling;   // start at the bottom
            return b;
        }

        /// <summary>WPF Bubble ctor with a Chaos spec, FloatUp only: the spec's size, the chaos
        /// pace (bigger drifts slower, SpeedMult, CHAOS_SPEED_MULT), the ambient wobble and rise.</summary>
        public static AmbientBubble SpawnChaos(Random r, int screen, double areaX, double areaY, double areaW, double areaH,
            double scaling, Chaos.EffectBubbleSpec spec)
        {
            var b = new AmbientBubble { Screen = screen, Clickable = true, Spec = spec, Size = spec.SizePx };
            if (spec.IsLive) b.FuseTotalMs = b.FuseRemainingMs = Math.Max(1, spec.FuseMs);   // WPF :3362
            b._speed = (1.0 + r.NextDouble()) * Math.Clamp(1.4 - (b.Size - 150) / 220.0, 0.6, 1.4)
                       * Math.Max(0.1, spec.SpeedMult) * Chaos.ChaosTuning.CHAOS_SPEED_MULT;
            b._animType = r.Next(4);
            b._wobbleOffset = r.NextDouble() * 100;
            b.Angle = r.Next(360);
            b._top = areaY / scaling - b.Size - 50;
            b.X = b._startX = (areaX + r.Next(50, (int)Math.Max(100, areaW - b.Size - 50))) / scaling;
            b.Y = (areaY + areaH) / scaling;
            return b;
        }

        /// <summary>WPF DANGER_TELEGRAPH_MS: the last stretch of a live fuse breathes faster and deeper.</summary>
        public const double DangerTelegraphMs = 1200;

        /// <summary>WPF _dangerFactor (BubbleService.cs:4155): 0 until the last DangerTelegraphMs of a
        /// live fuse, then ramps to 1 at fuse-out. Visual only.</summary>
        public double DangerFactor
        {
            get
            {
                if (Spec is not { IsLive: true } || FuseTotalMs <= 0) return 0;
                double dangerMs = Math.Min(FuseTotalMs, DangerTelegraphMs);
                return FuseRemainingMs < dangerMs ? Math.Clamp(1.0 - FuseRemainingMs / Math.Max(1, dangerMs), 0, 1) : 0;
            }
        }

        /// <summary>Draw scale this step: the pop scale plus WPF's breathe (:4231): 0.06 x sin(7.5 t),
        /// faster and deeper as the danger ramps; video/htlink breathe 60% calmer (:3233).</summary>
        public double DrawScale
        {
            get
            {
                double calm = Spec?.VariantId is "video" or "htlink" ? 0.4 : 1.0, d = DangerFactor;
                return Scale + (0.06 + d * 0.0225 * calm) * Math.Sin(_timeAlive * (7.5 + d * 9.0 * calm) + _wobbleOffset);
            }
        }

        /// <summary>WPF's three fuse-ring phases (BubbleService.cs:4166-4190) as ARGB, the ring's
        /// opacity in the alpha: yellow while burning, a yellow/red flash from RING_FLASH_FROM_MS,
        /// solid red with a quickening pulse in the brink window.</summary>
        public uint FuseRingArgb
        {
            get
            {
                double brinkMs = Math.Min(FuseTotalMs * 0.30, Chaos.ChaosTuning.RING_BRINK_MS);
                double flashMs = Math.Min(FuseTotalMs * 0.65, Chaos.ChaosTuning.RING_FLASH_FROM_MS);
                if (FuseRemainingMs <= brinkMs)
                {
                    double pulse = 0.5 + 0.5 * Math.Sin(_timeAlive * (18.0 + DangerFactor * 34.0));
                    return Argb(0.65 + 0.35 * pulse, 255, 45, 45);
                }
                if (FuseRemainingMs <= flashMs)
                {
                    double f = 0.5 + 0.5 * Math.Sin(_timeAlive * 14.0);
                    return Argb(0.8 + 0.2 * f, 255, (byte)(45 + (210 - 45) * f), (byte)(45 + (40 - 45) * f));
                }
                return Argb(1.0, 255, 210, 40);
            }
        }

        private static uint Argb(double opacity, byte r, byte g, byte b) =>
            ((uint)Math.Round(Math.Clamp(opacity, 0, 1) * 255) << 24) | ((uint)r << 16) | ((uint)g << 8) | b;

        public double CenterX => X + Size / 2;
        public double CenterY => Y + Size / 2;

        /// <summary>WPF ContainsPx: inside the hit disc (the sprite's own size for a plain bubble).</summary>
        public bool Contains(double x, double y)
        {
            double dx = x - CenterX, dy = y - CenterY, r = Size / 2;
            return dx * dx + dy * dy <= r * r;
        }

        /// <summary>One logical step. <paramref name="held"/> and the cursor (global DIPs) feed a
        /// running defuse channel; a channel pins the bubble and pauses the fuse (WPF :3796).</summary>
        public Result Step(bool held = false, double cursorX = 0, double cursorY = 0, DateTime nowUtc = default)
        {
            if (Popping)
            {
                // WPF BubbleService.cs:3791-3814: particles move before gravity and fade.
                for (var i = 0; i < Sparkles.Length; i++)
                {
                    ref var sp = ref Sparkles[i];
                    sp.X += sp.VelX;
                    sp.Y += sp.VelY;
                    sp.VelY += 0.15;
                    sp.Alpha -= Lucky ? 0.04 : 0.06;
                }
                // Pop animation - expand and fade; lucky pops linger ~50% longer.
                Scale += Deflating ? -0.03 : 0.04;
                Fade -= Lucky ? 0.044 : 0.066;
                Angle += 2;
                return Fade <= 0 ? Result.Done : Result.Alive;
            }
            if (Channeling) return TickChannel(held, cursorX, cursorY, nowUtc);
            // WPF :4147-4193: the fuse burns 32 ms a step; fuse-out detonates.
            if (Spec is { IsLive: true } && FuseRemainingMs > 0)
            {
                FuseRemainingMs -= FuseStepMs;
                if (FuseRemainingMs <= 0) return GoOff(null);
            }
            _timeAlive += 0.02;
            double offset = 0;
            switch (_animType)
            {
                case 0: offset = Math.Sin(_timeAlive * 6) * 25; Angle = (Angle + 0.34) % 360; break;
                case 1: offset = Math.Sin(_timeAlive * 7.5) * 30; Angle = (Angle + 0.14) % 360; break;
                case 2: offset = Math.Cos(_timeAlive * 5.4) * 25; Angle = (Angle - 0.66) % 360; break;
                case 3: offset = Math.Sin(_timeAlive * 3) * 30 + Math.Cos(_timeAlive * 6) * 15; Angle = (Angle + 0.54) % 360; break;
            }
            Y -= _speed;
            X = _startX + offset;
            if (Y >= _top) return Result.Alive;
            return Spec is { IsLive: true } ? GoOff(null) : Result.Missed;   // WPF :4212: a live that escapes detonates
        }
    }

    /// <summary>
    /// The plain ambient field of WPF BubbleService: the concurrent cap, the spawn cadence, the
    /// pop reward (lucky roll, daily XP bucket) and one logical step for every bubble. Not a
    /// singleton on purpose: the head owns one per run. UI-thread only.
    /// </summary>
    public sealed class AmbientBubbleField
    {
        /// <summary>WPF MAX_BUBBLES_HOST: the shared-host cap, which is how every bubble renders here.
        /// Solid mode off is WPF's per-window path, capped at 3 (MAX_BUBBLES).</summary>
        public static int Cap(AppSettings s) => s.BubbleSharedHost ? 40 : 3;

        /// <summary>WPF Start/RefreshFrequency: BubblesFrequency is per minute.</summary>
        public static TimeSpan SpawnInterval(int perMinute) => TimeSpan.FromMilliseconds(60000.0 / Math.Max(1, perMinute));

        /// <summary>WPF STEP_MS: the logical step gate (~30 fps).</summary>
        public const double StepMs = 30.0;

        public readonly List<AmbientBubble> Bubbles = new();
        public readonly Random Random;
        public AmbientBubbleField(Random? random = null) => Random = random ?? new Random();
        /// <summary>WPF SetChaosFrozen: a held Chaos run's live bubbles do not move (a pop burst still plays).</summary>
        public bool HoldChaos;
        /// <summary>WPF ChaosMouseHeld + CursorPx: the player's press, in global DIPs, for a defuse channel.</summary>
        public bool Held;
        public double CursorX, CursorY;
        /// <summary>The wall clock a defuse hold is measured on (WPF DateTime.UtcNow); tests step it.</summary>
        public Func<DateTime> Clock = () => DateTime.UtcNow;
        /// <summary>A live chaos bubble went off (fuse-out, escape, or a broken hold: see BrokenReason).</summary>
        public Action<AmbientBubble>? ChaosDetonated;
        /// <summary>A live chaos bubble was snapped (held to the end, or swept).</summary>
        public Action<AmbientBubble>? ChaosDefused;

        /// <summary>A press on a live bubble (WPF OnPlayerPress): starts its channel, or sets it off.</summary>
        public void PressLive(AmbientBubble b, bool canChannel, double cursorX, double cursorY)
        {
            if (HoldChaos) return;   // manual pause swallows every press
            Held = true; CursorX = cursorX; CursorY = cursorY;
            if (b.Press(canChannel, Clock()) == AmbientBubble.Result.Detonated) ChaosDetonated?.Invoke(b);
        }

        /// <summary>WPF DefuseAllLive: snap every live chaos bubble on the field (full pay).</summary>
        public void DefuseAllLive()
        {
            foreach (var b in Bubbles.ToArray())
                if (b.Snap()) ChaosDefused?.Invoke(b);
        }

        public bool CanSpawn(AppSettings s) => Bubbles.Count < Cap(s);

        /// <summary>WPF AwardAmbientPop's pure half: roll lucky (5% for 20x with the lucky_bubbles
        /// skill), draw 5 x multiplier from the daily bucket. A lucky roll that paid nothing gets the
        /// ordinary pop. Returns the XP to award; the head plays the sound and credits it.</summary>
        public int Pop(AmbientBubble b, AppSettings s)
        {
            if (b.Popping) return 0;
            b.Popping = true;
            var multiplier = SkillTreeRules.HasSkill(s, "lucky_bubbles") && Random.NextDouble() < 0.05 ? 20 : 1;
            var paid = AmbientBubbleXp.Take(s, 5 * multiplier);
            b.Lucky = multiplier > 1 && paid > 0;
            // WPF BubbleService.cs:1105-1108: dry lucky rolls lose gold, boost respects FlashGlow.
            b.StartSparkles(Random, SkillTreeRules.GetSparkleBoostTier(s) > 0 && s.FlashGlowEnabled);
            return paid;
        }

        /// <summary>The topmost live, clickable bubble under a point on one screen, or null (WPF PopTopmostAt).</summary>
        public AmbientBubble? HitTest(int screen, double x, double y)
        {
            for (var i = Bubbles.Count - 1; i >= 0; i--)
            {
                var b = Bubbles[i];
                if (b.Screen == screen && b.Clickable && !b.Popping && b.Contains(x, y)) return b;
            }
            return null;
        }

        /// <summary>One logical step. Missed and finished bubbles are removed; returns the misses.
        /// Allocation-free.</summary>
        public int Step()
        {
            var missed = 0;
            var now = Clock();
            for (var i = Bubbles.Count - 1; i >= 0; i--)
            {
                if (i >= Bubbles.Count) continue;   // a callback cleared the field
                var b = Bubbles[i];
                if (HoldChaos && b.Spec != null && !b.Popping) { b.CancelChannel(); continue; }
                var r = b.Step(Held, CursorX, CursorY, now);
                if (r == AmbientBubble.Result.Alive) continue;
                if (r == AmbientBubble.Result.Detonated) { ChaosDetonated?.Invoke(b); continue; }
                if (r == AmbientBubble.Result.Defused) { ChaosDefused?.Invoke(b); continue; }
                if (r == AmbientBubble.Result.Missed) missed++;
                Bubbles.RemoveAt(i);
            }
            return missed;
        }
    }

    /// <summary>The Bubble Pop control seam (WPF App.Bubbles): the engine and the card start,
    /// stop and re-time it; the head owns the surface. Unseeded = silent no-op.</summary>
    public static class CoreBubbles
    {
        public static volatile Action? StartAction;
        public static volatile Action? StopAction;
        public static volatile Action? RefreshFrequencyAction;

        public static void Start() { try { StartAction?.Invoke(); } catch { } }
        public static void Stop() { try { StopAction?.Invoke(); } catch { } }
        public static void RefreshFrequency() { try { RefreshFrequencyAction?.Invoke(); } catch { } }
        /// <summary>WPF BubbleService.PauseAndClear / Resume: a minigame (bubble count) holds the field.
        /// Resume restarts only what Pause stopped, and never after a Stop.</summary>
        public static volatile Action? PauseAction, ResumeAction;
        public static void Pause() { try { PauseAction?.Invoke(); } catch { } }
        public static void Resume() { try { ResumeAction?.Invoke(); } catch { } }
    }
}
