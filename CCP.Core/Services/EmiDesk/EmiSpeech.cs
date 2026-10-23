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
    public static readonly IReadOnlyDictionary<string, string> FaceBodyFrame =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // celebration
            ["^_^"] = "celebration",
            ["^___^"] = "celebration",
            ["^_~"] = "celebration",
            ["\\o/"] = "celebration",
            ["GG"] = "celebration",
            ["LV UP"] = "celebration",
            ["\u2605\u2605\u2605"] = "celebration",
            [":D"] = "celebration",
            ["XD"] = "celebration",
            // sad
            [";_;"] = "sad",
            ["T_T"] = "sad",
            ["x_x"] = "sad",
            ["(\u0CA5_\u0CA5)"] = "sad",
            ["(\u2716\u256D\u256E\u2716)"] = "sad",
            [":'("] = "sad",
            // shock
            ["o_o"] = "shock",
            ["(\u25C9_\u25C9)"] = "shock",
            ["(\u2299_\u2299)"] = "shock",
            [">_<"] = "shock",
            [">.<"] = "shock",
            ["!!!"] = "shock",
            ["???"] = "shock",
            ["#ERR"] = "shock",
            ["404"] = "shock",
            [":O"] = "shock",
            // smug
            ["\u00AC_\u00AC"] = "smug",
            ["(\u00AC\u203F\u00AC)"] = "smug",
            ["(\u0CA0\u203F\u0CA0)"] = "smug",
            ["(\u2310\u25A0_\u25A0)"] = "smug",
            ["( \u0361\u00B0 \u035C\u0296 \u0361\u00B0)"] = "smug",
            ["(\u25D4_\u25D4)"] = "smug",
            ["B)"] = "smug",
            [">:)"] = "smug",
            // pet
            ["(\uFF61\u2665\u203F\u2665\uFF61)"] = "pet",
            ["(\u273F\u25E1\u203F\u25E1)"] = "pet",
            ["(\u2267\u25E1\u2266)"] = "pet",
            ["(\u25E0\u203F\u25E0)"] = "pet",
            ["(\u25D5\u203F\u25D5)"] = "pet",
            ["*_*"] = "pet",
            ["\u2665\u2665\u2665"] = "pet",
            ["<3"] = "pet"
        };

    /// <summary>The pose a raw face string wears. Unpaired faces rest at idle.</summary>
    public static string FrameForFace(string? text)
        => text != null && FaceBodyFrame.TryGetValue(text, out var f) ? f : "idle";


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
