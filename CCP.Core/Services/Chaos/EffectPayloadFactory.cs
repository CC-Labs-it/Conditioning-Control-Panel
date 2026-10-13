using System;

namespace ConditioningControlPanel.Services.Chaos;

/// <summary>
/// Builds <see cref="EffectPayload"/> instances by kind. <see cref="EffectPayload.Strength"/>
/// is set by the caller from bubble size — the factory only constructs.
/// (The PayloadConfig/PickWeighted weighted-pool layer was deleted 2026-06-12: nothing ever
/// called it — variant specs carry their own payload kinds — and the phantom tuning knobs
/// would have eaten a designer's afternoon for zero effect.)
/// </summary>
public static class EffectPayloadFactory
{
    private static readonly Random _rng = new();

    /// <summary>Construct a payload for a specific kind (Strength set later by caller).</summary>
    public static EffectPayload Build(EffectBubblePayloadKind kind) => kind switch
    {
        EffectBubblePayloadKind.Flash       => new FlashPayload(),
        EffectBubblePayloadKind.Subliminal  => new SubliminalPayload(),
        EffectBubblePayloadKind.Overlay     => new OverlayPayload(PickOverlayKind()),
        EffectBubblePayloadKind.Video       => new VideoPayload(),
        EffectBubblePayloadKind.HtLink      => new HtLinkPayload(),
        EffectBubblePayloadKind.Audio        => new AudioPayload(),
        EffectBubblePayloadKind.BambiFreeze  => new BambiFreezePayload(),
        EffectBubblePayloadKind.BouncingText => new BouncingTextPayload(),
        EffectBubblePayloadKind.GifCascade   => new GifCascadePayload(),
        _                                    => new FlashPayload(),
    };

    private static string PickOverlayKind()
    {
        string[] kinds = { "pink_filter", "spiral", "braindrain" };
        return kinds[_rng.Next(kinds.Length)];
    }
}
