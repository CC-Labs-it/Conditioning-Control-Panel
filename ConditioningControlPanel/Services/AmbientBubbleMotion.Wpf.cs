using System;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Prizes;

namespace ConditioningControlPanel.Services;

/// <summary>WPF live reads over the shared Core motion rules.</summary>
public static class AmbientBubbleMotion
{
    public static bool IsOwned(BubbleMotionStyle style, bool rainOwned, bool spiralOwned) =>
        BubbleMotionRoll.IsOwned(style, rainOwned, spiralOwned);
    public static BubbleMotionStyle Resolve(BubbleMotionStyle picked, bool rainOwned, bool spiralOwned,
                                            MotionLevel level, double roll) =>
        BubbleMotionRoll.Resolve(picked, rainOwned, spiralOwned, level, roll);
    public static double SpeedMult(MotionLevel level) => BubbleMotionRoll.SpeedMult(level);

    // ---- live reads (UI thread, at spawn / repaint) ----

    public static bool RainOwned => PrizeGrants.IsGranted(PrizeGrants.BubbleRain);
    public static bool SpiralInOwned => PrizeGrants.IsGranted(PrizeGrants.BubbleSpiralIn);
    /// <summary>Either ambient prize style. One spelling, in <see cref="V2Badges"/>, because the
    /// dashboard tile and the side rail's chips light off the very same question.</summary>
    public static bool AnyV2Owned => V2Badges.BubbleOwned();

    /// <summary>The concrete style for one ambient spawn, from the current picker + grants + motion level.</summary>
    public static BubbleMotionStyle RollForSpawn(Random random) =>
        Resolve(App.Settings?.Current?.BubbleMotionStyle ?? BubbleMotionStyle.FloatUp,
                RainOwned, SpiralInOwned, MotionFx.Level, random.NextDouble());
}
