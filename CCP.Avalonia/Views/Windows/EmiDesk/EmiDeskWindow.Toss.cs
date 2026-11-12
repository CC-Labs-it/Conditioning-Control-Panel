using System;
using System.Threading;
using Avalonia.Media;
using ConditioningControlPanel.Services.EmiDesk;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;

// PORTED from ConditioningControlPanel/Windows/EmiDesk/EmiDeskWindow.Toss.cs: picked up, put down,
// thrown. The numbers, the throw detection and the landing curve are Core EmiTossRules /
// EmiFlingDetector, shared with WPF.
//
// Land faces and the landing curve are retained; throws now play Core's dizzy chain.

public partial class EmiDeskWindow
{
    /// <summary>The throw clock; tests step it.</summary>
    internal static TimeProvider TossClock = TimeProvider.System;

    internal static Action<string, object?> TossMoment = (moment, context) => EmiDeskService.Instance.Fire(moment, context);

    private readonly EmiFlingDetector _toss = new();

    /// <summary>The landing squash pivots on her FEET; WPF moved SquashScale.CenterY there for the
    /// landing. Avalonia's ScaleTransform has no centre, so this slot right after the squash adds
    /// the (1 - sy) x feet offset that the centre would have.</summary>
    private readonly TranslateTransform _landShift = new();

    private DateTime TossNow => TossClock.GetUtcNow().UtcDateTime;

    /// <summary>WPF Toss.cs:54. The press just became a real drag.</summary>
    private void OnPickedUp()
    {
        try
        {
            _toss.PickUp(Position.Y / SafeScale());
            EmiSfx.Lift();
            if (!ChainLive && _wobbleFace == null) DrawFace(EmiTossRules.PickupFace);
            TossMoment("dragged", null);
        }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] pickup failed"); }
    }

    /// <summary>WPF Toss.cs:75. One wobble frame of throw detection (the vertical half).</summary>
    private void NoteTossFrame(double dt, double vx)
    {
        try { _toss.Frame(Position.Y / SafeScale(), dt, vx, WobbleVelKeep, TossNow); }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] toss frame failed"); }
    }

    /// <summary>WPF Toss.cs:90. A real drag just ended, after the clamp and the placement save.</summary>
    private void OnPutDown()
    {
        try
        {
            var zoneRow = TossZoneRow();
            if (_toss.PutDown(TossNow))
            {
                int total = EmiState.NoteFling();
                EmiSfx.Thud();
                PlayLanding(thrown: true);
                var ctx = new { zoneRow, flings20 = total >= 20, flings50 = total >= 50 };
                PlayChain("dizzy", () => TossMoment("flung", ctx));
                return;
            }
            EmiSfx.Bump();
            PlayLanding(thrown: false);
            var dropCtx = new { zoneRow, flings20 = false, flings50 = false };
            if (ChainLive) TossMoment("dropped", dropCtx);
            else PlayChain(LandChain, () => TossMoment("dropped", dropCtx));
        }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] put down failed"); }
    }

    private string TossZoneRow()
    {
        try
        {
            var body = BodyScreenRect;
            var work = WorkAreaAt(body);
            return work is { } area
                ? EmiTossRules.ZoneRow(Math.Round(body.Y + body.Height / 2), area.Y, area.Height)
                : "mid";
        }
        catch { return "mid"; }
    }

    internal const int LandFaceMs = 600;
    private static readonly EmiChain LandChain = new("land", "PUT DOWN",
        new[] { new EmiFrame("^_^", LandFaceMs), new EmiFrame(EmiChains.RestFace, 160) },
        BodyFrame: "celebration");

    /// <summary>WPF Toss.cs:125. The landing squash from her feet, on the squash's own slot: it
    /// replaces a running click squash the way WPF's BeginAnimation did.</summary>
    private void PlayLanding(bool thrown)
    {
        try
        {
            if (_closingForGood) return;
            _squashAnim?.Cancel();
            _squashAnim?.Dispose();
            var cts = new CancellationTokenSource();
            _squashAnim = cts;
            cts.Token.Register(() => _landShift.Y = 0);   // a click squash that replaces it pivots from the middle again

            double feet = _bodyWidth * BodyAspect * EmiTossRules.FeetBelowOrigin;
            Tween(thrown ? EmiTossRules.ThudMs : EmiTossRules.LandMs, cts.Token, p =>
            {
                var (x, y) = EmiTossRules.LandingScale(thrown, p);
                _squashScale.ScaleX = x;
                _squashScale.ScaleY = y;
                _landShift.Y = (1 - y) * feet;
            });
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] landing squash failed");
            _landShift.Y = 0;
        }
    }
}
