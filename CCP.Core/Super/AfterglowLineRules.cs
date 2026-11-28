using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.SuperCore;

/// <summary>
/// The head-free half of the Afterglow line's host (WPF <c>AfterglowLineLayer</c>, Avalonia
/// <c>AfterglowLineOverlay</c>): the option box read into the line, where the line sits, where
/// a ghost rises from and the band the fog is told to write the finished line into. Both heads
/// step and draw; every number they share lives here. Pure: no WPF, no Skia, no App.
/// </summary>
public static class AfterglowLineRules
{
    /// <summary>How often a moving line repaints, seconds: 30 pictures a second at most.</summary>
    public const double RepaintSeconds = 1 / 30.0;

    /// <summary>
    /// The option box, read live into the line: words per line, how long the line stays lit,
    /// ghost strength. True when the ghost strength moved, which repaints a waiting line.
    /// </summary>
    public static bool Apply(AfterglowLine line, AppSettings? s)
    {
        if (s == null) return false;
        line.Words = AfterglowLineOptions.ClampWords(s.AfterglowLineWords);
        line.HoldSeconds = AfterglowLineOptions.ClampHold(s.AfterglowLineHold);
        double ghost = AfterglowLineOptions.GhostAlpha(s.AfterglowGhostOpacity);
        if (ghost == line.Ghost) return false;
        line.Ghost = ghost;
        return true;
    }

    /// <summary>The band the line is drawn in, in the monitor's pixels: baseline at 11 percent from
    /// the bottom, words up to 8 percent tall. The screen reader skips hits inside it while words are up.</summary>
    public static System.Drawing.Rectangle LineBand(System.Drawing.Rectangle b)
        => new(b.X + (int)(b.Width * 0.03), b.Bottom - (int)(b.Height * 0.25), (int)(b.Width * 0.94), (int)(b.Height * 0.23));

    /// <summary>The band across the middle of the monitor where the fog writes a finished line.</summary>
    public static (double Left, double Top, double Right, double Bottom) FogBand(System.Drawing.Rectangle b)
        => (b.X + b.Width * 0.06, b.Y + b.Height * 0.38, b.X + b.Width * 0.94, b.Y + b.Height * 0.66);

    /// <summary>
    /// Where a word flashes, so where its ghost rises: the middle of the monitor, a little above
    /// centre, or the eye of the Whirlpool while the pool is up on this monitor and its "Words"
    /// switch lets it take them. Caller holds whatever lock guards <paramref name="stage"/>.
    /// </summary>
    public static (double X, double Y) WordsFrom(SuperStage stage, double left, double top, double width, double height)
    {
        if (stage.PoolTakesWords && stage.Pool.Size > 0.3
            && stage.Pool.X >= left && stage.Pool.X < left + width && stage.Pool.Y >= top && stage.Pool.Y < top + height)
            return (stage.Pool.X, stage.Pool.Y);
        return (left + width / 2, top + height * 0.46);
    }
}
