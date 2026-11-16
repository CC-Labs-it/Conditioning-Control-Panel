namespace ConditioningControlPanel.SuperCore;

/// <summary>What the Whirlpool drags in. Each can be switched off on its own.</summary>
public enum PullKind { Picture, Bubble }

/// <summary>
/// The Whirlpool's options in the gold box (owner, 2026-11-15): every clamp, default and
/// slider-to-number mapping. Defaults reproduce the pool as it shipped. Opacity and monitor are
/// the spiral's own and are not repeated here.
/// </summary>
public static class WhirlpoolOptions
{
    /// <summary>The mass the flash layer pulls its pictures with. <see cref="SuperStage.Pull(double, double, double)"/>
    /// reads a pull of exactly this mass as a picture and any other as a bubble, until the callers
    /// say what they pull through <see cref="SuperStage.Pull(double, double, double, PullKind)"/>.</summary>
    public const double PictureMass = 1.4;

    /// <summary>Which kind a pull with no kind given is (see <see cref="PictureMass"/>).</summary>
    public static PullKind KindOf(double mass) => mass == PictureMass ? PullKind.Picture : PullKind.Bubble;

    /// <summary>"Pool size": percent of the stock largest size. 100 = as shipped.</summary>
    public const int SizeMin = 50, SizeMax = 150, SizeDefault = 100;
    public static int ClampSize(int pct) => Math.Clamp(pct, SizeMin, SizeMax);
    public static double ReachScale(int pct) => ClampSize(pct) / 100.0;

    /// <summary>"New spiral every", seconds a Loom spiral holds before it melts into the next.
    /// 18 = as shipped. Never shorter than the melt itself.</summary>
    public const int HoldMin = 8, HoldMax = 60, HoldDefault = 18;
    public static int ClampHold(int seconds) => Math.Clamp(seconds, HoldMin, HoldMax);
}
