using System;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Chaos;
using Serilog;
namespace ConditioningControlPanel.Services;
/// <summary>WPF BubbleService.RollTriggerSpec / BuildTriggerSpec, ambient only.</summary>
public static class AmbientBubblePayloads
{
    public const int MaxTriggers = 4;
    /// <summary><paramref name="v2Owned"/> is any Bubbles v2 grant, read from PrizeGrants by the caller:
    /// it (and BubbleBrainDrainEnabled) adds the Brain Drain bubble as one more equal entry (WPF BubbleService.cs:1150).</summary>
    public static EffectBubbleSpec? Roll(AppSettings s, Random random, int activeTriggers, bool v2Owned = false)
    {
        if (!s.BubbleTriggersEnabled || activeTriggers >= MaxTriggers) return null;
        var selected = (s.BubbleTriggerVariants ?? new())
            .Where(id => id != MagnetBubble.VariantId && id != BrainDrainBubble.VariantId).ToArray();
        var ids = BrainDrainBubble.RollPool(selected, v2Owned, s.BubbleBrainDrainEnabled).ToArray();
        if (ids.Length == 0 || random.Next(100) >= Math.Clamp(s.BubbleTriggerChance, 0, 100)) return null;
        // WPF BuildTriggerSpec: the dashboard Motion setting; "Mixed" does not parse -> FloatUp.
        ChaosMotion? motion = Enum.TryParse<ChaosMotion>(s.ChaosMotionMode, out var m) ? m : null;
        return Build(ids[random.Next(ids.Length)], motion);
    }
    public static EffectBubbleSpec? Build(string id, ChaosMotion? motion = null)
    {
        // WPF BubbleService.cs:1202: benign, deep violet, breathes (AmbientBubble.DisplayFade).
        if (id == BrainDrainBubble.VariantId) return new EffectBubbleSpec
        {
            VariantId = id, Payload = new BrainDrainMeltPayload { Ambient = true }, SizePx = 220,
            Tint = 0xFF000000u | ((uint)BrainDrainBubble.TintR << 16) | ((uint)BrainDrainBubble.TintG << 8) | BrainDrainBubble.TintB,
            IsLive = false, FuseMs = 0, Motion = motion ?? ChaosMotion.FloatUp, TreatLifeMs = 7000,
        };
        if (id == "glitch") return new EffectBubbleSpec
        {
            VariantId = id, Payload = new OverlayPayload("braindrain", 0.30) { Strength = 60, DurationMult = 1.2, Ambient = true },
            SizePx = 200, Tint = 0xFF9A40FFu, Label = "GLITCH", IsLive = false,
            Motion = motion ?? ChaosMotion.FloatUp, TreatLifeMs = 7000,
        };
        var variant = ChaosBubbleVariants.All.FirstOrDefault(v => v.Id == id);
        if (variant == null) return null;
        var spec = ChaosBubbleVariants.Build(variant, 0.3, motionOverride: motion, ambient: true);
        spec.Payload.DurationMult = 2.5;
        return spec;
    }
}
/// <summary>UI-independent ambient payload delivery: heavy takeovers yield the input handler,
/// wait for rain, and recheck both stop and video gates before opening.</summary>
public sealed class AmbientPayloadRunner
{
    private int _generation;
    public void Stop() => _generation++;
    public void Run(EffectPayload payload, Func<bool> running, Func<bool> videoPlaying,
        Func<bool> raining, Action<Action> post, Action<Action> whenClear, Action<EffectPayload> fire)
    {
        var generation = _generation;
        bool Heavy() => payload.Kind is EffectBubblePayloadKind.Video or EffectBubblePayloadKind.HtLink;
        void Deliver()
        {
            if (generation != _generation || !running() || (Heavy() && videoPlaying())) return;
            try { fire(payload); }
            catch (Exception ex) { Log.Warning(ex, "Bubble: {Kind} payload failed", payload.Kind); }
        }
        if (!Heavy()) { Deliver(); return; }
        if (videoPlaying()) return;
        if (raining()) whenClear(Deliver);
        else post(Deliver);
    }
}

/// <summary>WPF OverlayService.cs:950-1070: keep the peak until the last timed hold ends.</summary>
public sealed class TimedOverlayHold
{
    private int _holds, _generation;
    public bool IsActive => _holds > 0;
    public double Opacity { get; private set; }
    public Func<bool> Acquire(double opacity)
    {
        Opacity = Math.Max(Opacity, Math.Clamp(opacity, 0, 1));
        _holds++;
        var generation = _generation;
        var released = false;
        return () =>
        {
            if (released || generation != _generation) return false;
            released = true;
            if (--_holds == 0) Opacity = 0;
            return true;
        };
    }
    public void Clear() { _generation++; _holds = 0; Opacity = 0; }
}
