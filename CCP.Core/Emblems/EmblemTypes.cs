namespace ConditioningControlPanel.Emblems;

/// <summary>
/// What an option MEANS, which picks its emblem (owner 2026-11-24, Readable Rows pilot). One meaning,
/// one picture, in every box: a stopwatch is "how often" everywhere. Art is ported 1:1 from the
/// approved mockup's <c>E</c> table (handoff-1124/readable-rows/index.html); the mockup kinds
/// <c>words</c> (live text "Aa") and <c>sound</c> are not ported: <see cref="Words"/> is the
/// mockup's <c>wordslip</c> pill.
/// </summary>
public enum EmblemKind
{
    Often, Many, Most, Long, Big, Clear, Loud, Fast, Much, Random, Click, Stay,
    After, Splits, Link, Where, Screens, Colour, When, Safe, Motion, Reacts, Eye, Glow, Solid,
    Keepout, Corners, Drag, Shatter, Jackpot, Flip,
    // Second batch (overnight sweep 2026-11-24): the mockup's wordslip, perf, lock, layers, loop,
    // folder, mic, plug, power, bell, gentle, persona, ramp, spiral, fog, stamp, lamp, card, target, cam.
    Words, Perf, Lock, Layers, Loop, Folder, Mic, Plug, Power, Bell,
    Gentle, Persona, Ramp, Spiral, Fog, Stamp, Lamp, Card, Target, Cam,
}

/// <summary>What a stack is made of: the box it sits in (photos, clips, bubbles, words).</summary>
public enum EmblemSubject { Image, Video, Bubble, Word }

/// <summary>
/// The paint a layer asks for. A style maps each ink to a colour (<see cref="EmblemPalette.Ink"/>);
/// <see cref="Fixed"/> carries its own ARGB on the layer (the Colour emblem's three dots).
/// </summary>
public enum EmblemInk { Main, Cut, Soft, Paper, Pic, Tape, Fixed }

/// <summary>
/// The sticker edge around an emblem (ccp-bugs #1742): <see cref="White"/> is the approved look,
/// <see cref="Dark"/> a near-black outline, <see cref="DarkRim"/> that outline with a thin light rim
/// outside it. Passes and widths: <see cref="EmblemPalette.EdgePasses"/>.
/// </summary>
public enum EmblemEdge { White, Dark, DarkRim }

/// <summary>
/// One drawing pass of an emblem. <paramref name="PathData"/> is SVG path syntax in the 32 x 32
/// space with every transform already baked in (absolute M/L/C/Q/A/Z only), so a head only parses
/// it. <paramref name="StrokeWidth"/> 0 means a fill; otherwise it is a stroke of that width in
/// grid units with round joins. <paramref name="FixedArgb"/> is set only for <see cref="EmblemInk.Fixed"/>.
/// Fills use the SVG nonzero rule (WPF: prefix <c>F1 </c> before Geometry.Parse).
/// </summary>
public sealed record EmblemLayer(string PathData, EmblemInk Ink, double StrokeWidth, bool RoundCaps, uint? FixedArgb)
{
    public bool IsStroke => StrokeWidth > 0;
}
