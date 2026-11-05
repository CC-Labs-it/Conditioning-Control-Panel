using System;

namespace ConditioningControlPanel.Services.Companion;

/// <summary>
/// How far a dressing-room bake is (fix wave F1c): clips finished (baked or skipped) of the clips the
/// pack must cover, plus how far into the clip in hand. One bake is about five minutes for 17 clips,
/// so the dressing room, its window title and its taskbar button all read this.
/// </summary>
internal readonly record struct RainBakeProgress(int ClipsDone, int ClipsTotal, double ClipFraction)
{
    /// <summary>0..1 over the whole bake; never runs backwards while clips finish in order.</summary>
    public double Fraction => ClipsTotal <= 0 ? 0
        : Math.Clamp((Math.Min(ClipsDone, ClipsTotal) + (ClipsDone >= ClipsTotal ? 0 : Math.Clamp(ClipFraction, 0, 1))) / ClipsTotal, 0, 1);

    /// <summary>The clip in hand, 1-based ("3 of 17"); the total once every clip is in.</summary>
    public int ClipShown => ClipsTotal <= 0 ? 0 : Math.Min(ClipsTotal, ClipsDone + 1);

    public int Percent => (int)Math.Floor(Fraction * 100);

    /// <summary>Fills a <c>{n}</c> / <c>{total}</c> line (the same template the page fills).</summary>
    public string Fill(string template)
        => (template ?? "").Replace("{n}", ClipShown.ToString()).Replace("{total}", ClipsTotal.ToString());
}
