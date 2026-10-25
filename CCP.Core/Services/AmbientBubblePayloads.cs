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

    public static EffectBubbleSpec? Roll(AppSettings s, Random random, int activeTriggers)
    {
        if (!s.BubbleTriggersEnabled || activeTriggers >= MaxTriggers) return null;
        // ponytail: v2 ownership and motion variants remain with the bubble-motion lane.
        var ids = (s.BubbleTriggerVariants ?? new()).Where(id => id != MagnetBubble.VariantId).ToArray();
        if (ids.Length == 0 || random.Next(100) >= Math.Clamp(s.BubbleTriggerChance, 0, 100)) return null;
        return Build(ids[random.Next(ids.Length)]);
    }

    public static EffectBubbleSpec? Build(string id)
    {
        if (id == "glitch") return new EffectBubbleSpec
        {
            VariantId = id, Payload = new OverlayPayload("braindrain", 0.30) { Strength = 60, DurationMult = 1.2, Ambient = true },
            SizePx = 200, Tint = 0xFF9A40FFu, Label = "GLITCH", IsLive = false,
            Motion = ChaosMotion.FloatUp, TreatLifeMs = 7000,
        };
        var variant = ChaosBubbleVariants.All.FirstOrDefault(v => v.Id == id);
        if (variant == null) return null;
        var spec = ChaosBubbleVariants.Build(variant, 0.3, motionOverride: ChaosMotion.FloatUp, ambient: true);
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
