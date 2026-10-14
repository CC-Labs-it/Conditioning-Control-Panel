using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services.EmiDesk;

/// <summary>One frame of a chain: a face, a hold in ms, and the frame's options.</summary>
/// <param name="Text">The kaomoji (or special string) on the glass for this frame.</param>
/// <param name="Ms">How long the frame is held, in milliseconds.</param>
/// <param name="Small">THINKING dots mode (30 % size, -28 % lift).</param>
/// <param name="SetsBubble">
/// True when this frame carries a bubble instruction. face.js/chains.js distinguish
/// "no bubble key" (leave the bubble alone) from "bubble: null" (clear it); C# has no
/// `'bubble' in o`, so the flag carries that difference.
/// </param>
/// <param name="Bubble">The bubble text, or null to clear it, when <paramref name="SetsBubble"/>.</param>
public sealed record EmiFrame(string Text, int Ms, bool Small = false, bool SetsBubble = false, string? Bubble = null);

/// <summary>
/// A chain: the sequence, the pose held for the whole run, and the one-shot fx / body move.
/// Ported verbatim from <c>Resources/web/arcademy/emi/chains.js</c>.
/// </summary>
/// <param name="Id">Chain id, the key in <see cref="EmiChains.Chains"/>.</param>
/// <param name="Label">Human label from the lock (kept for logs and the dev pass).</param>
/// <param name="Seq">The frames.</param>
/// <param name="Fx">One-shot fx kind at frame 0 (<c>hearts</c>, <c>sparks</c>, <c>tears</c>, <c>storm</c>, <c>bang</c>).</param>
/// <param name="Move">One-shot body move at frame 0 (<c>bounce</c>, <c>nod</c>, <c>droop</c>, <c>shiver</c>, <c>thud</c>).</param>
/// <param name="Flat">Suppress the 90 degree rotation of classic sideways faces for every frame.</param>
/// <param name="BodyFrame">The pose held for the WHOLE chain (a per-frame pose reads as a broken sprite).</param>
public sealed record EmiChain(
    string Id,
    string Label,
    IReadOnlyList<EmiFrame> Seq,
    string? Fx = null,
    string? Move = null,
    bool Flat = false,
    string? BodyFrame = null);

/// <summary>The locked WPF speech cadence, shared by both heads.</summary>
public static class EmiSpeech
{
    public static EmiChain MakeSay(string? line, string reactionFace = "^_^", int holdMs = 1800)
        => new("say", "SAY", new[]
        {
            new EmiFrame("0_0", 420, SetsBubble: true, Bubble: "."),
            new EmiFrame("0_0", 420, SetsBubble: true, Bubble: ".."),
            new EmiFrame("0_0", 520, SetsBubble: true, Bubble: "..."),
            new EmiFrame(string.IsNullOrEmpty(reactionFace) ? "^_^" : reactionFace,
                Math.Max(400, holdMs), SetsBubble: true, Bubble: line ?? string.Empty),
            new EmiFrame("0_0", 200, SetsBubble: true)
        });

    public static int SayHoldMs(string? line) => Math.Max(4050, 1890 + 61 * (line?.Length ?? 0));
}
