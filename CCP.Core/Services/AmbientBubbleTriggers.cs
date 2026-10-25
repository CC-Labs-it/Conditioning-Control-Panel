using System;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Chaos;

namespace ConditioningControlPanel.Services;

/// <summary>WPF BubbleService.RollTriggerSpec / BuildTriggerSpec, at the ambient spawn boundary.</summary>
public static class AmbientBubbleTriggers
{
    public static EffectBubbleSpec? Roll(AppSettings s, Random random, int effectsOnField, bool v2Owned)
    {
        if (!s.BubbleTriggersEnabled || effectsOnField >= 4) return null;
        var ids = BrainDrainBubble.RollPool(s.BubbleTriggerVariants, v2Owned, s.BubbleBrainDrainEnabled)
            .Where(id => id != MagnetBubble.VariantId).ToArray();
        if (ids.Length == 0 || random.Next(100) >= Math.Clamp(s.BubbleTriggerChance, 0, 100)) return null;
        return Build(ids[random.Next(ids.Length)], s.ChaosMotionMode);
    }

    public static EffectBubbleSpec? Build(string id, string motionMode)
    {
        ChaosMotion? motion = Enum.TryParse<ChaosMotion>(motionMode, out var m) ? m : null;
        if (id == MagnetBubble.VariantId) return null; // Retired in WPF, including saved selections.
        if (id is "glitch" or BrainDrainBubble.VariantId)
            return new EffectBubbleSpec
            {
                VariantId = id,
                Payload = id == "glitch"
                    ? new OverlayPayload("braindrain", braindrainOpacity: 0.30) { Strength = 60, DurationMult = 1.2 }
                    : new BrainDrainMeltPayload(),
                SizePx = id == "glitch" ? 200 : 220,
                Tint = id == "glitch" ? 0xFF9A40FFu : 0xFF6A22A8u,
                Label = id == "glitch" ? "GLITCH" : "",
                IsLive = false, FuseMs = 0, Motion = motion ?? ChaosMotion.FloatUp, TreatLifeMs = 7000
            };
        var variant = ChaosBubbleVariants.All.FirstOrDefault(v => v.Id == id);
        if (variant == null) return null;
        var spec = ChaosBubbleVariants.Build(variant, intensity: 0.3, motionOverride: motion, ambient: true);
        spec.Payload.DurationMult = 2.5;
        return spec;
    }
}
