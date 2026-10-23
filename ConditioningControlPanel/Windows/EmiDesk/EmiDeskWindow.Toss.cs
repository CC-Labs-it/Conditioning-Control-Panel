using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ConditioningControlPanel.Services.EmiDesk;
using Serilog;

// NAMESPACE TRAP: the FLAT ConditioningControlPanel namespace, same as every other file under
// Windows\. See the header of EmiDeskWindow.xaml.cs before "tidying" this.
namespace ConditioningControlPanel;

/// <summary>
/// PICKED UP, PUT DOWN, THROWN. The half of the drag the wobble never had: what she does at the two
/// ends of it. Ported from the campus widget (<c>Resources/web/arcademy/emi/widget.js</c>
/// <c>beginDrag</c> / <c>onUp</c> and emi.css's <c>bounce</c> / <c>thud</c>), because the owner's
/// report on 2026-09-25 was that the desk EMI "is missing the juice the arcademy has": she slid
/// around the desktop and landed like a sticker.
///
/// <list type="bullet">
///   <item><b>Pickup</b> - the lift cue, a startled <c>o_o</c>, and now and then a drag line.</item>
///   <item><b>Put down</b> - the bump cue, a squash from the feet that springs back with one
///         overshoot, <c>^_^</c>, and sometimes a remark about where she was left.</item>
///   <item><b>Fling</b> - fast enough for long enough, and the drop is a thud, a deeper squash, the
///         <c>dizzy</c> chain, and then (a third of the time) a line about the flight.</item>
/// </list>
///
/// <para>Like the squash, the landing owns <c>SquashScale</c> and never waits on the line engine:
/// the body feedback is unconditional, only the words are rate-limited.</para>
/// </summary>
public partial class EmiDeskWindow
{
    // ---------------------------------------------------------------- the numbers

    // The numbers and the throw detection live in Core (EmiTossRules / EmiFlingDetector) so the
    // Avalonia desk lands her the same way.
    private const double LandSquashX = EmiTossRules.LandSquashX, LandSquashY = EmiTossRules.LandSquashY;
    private const int LandMs = EmiTossRules.LandMs;
    private const double ThudSquashX = EmiTossRules.ThudSquashX, ThudSquashY = EmiTossRules.ThudSquashY;
    private const int ThudMs = EmiTossRules.ThudMs;
    private const string PickupFace = EmiTossRules.PickupFace;

    private static readonly EmiChain LandChain = new(
        "land", "PUT DOWN",
        new[] { new EmiFrame("^_^", 600), new EmiFrame(EmiChains.RestFace, 160) },
        BodyFrame: "celebration");

    // ---------------------------------------------------------------- the state

    private readonly EmiFlingDetector _toss = new();

    // ---------------------------------------------------------------- pickup

    /// <summary>The press just became a real drag (past <see cref="DragThresholdDip"/>).</summary>
    private void OnPickedUp()
    {
        try
        {
            _toss.PickUp(Top);

            EmiSfx.Lift();
            if (!_player.IsLive && _wobbleFace == null) DrawFace(PickupFace);
            App.EmiDesk?.Fire("dragged");
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] pickup failed");
        }
    }

    /// <summary>
    /// One wobble frame's worth of throw detection. Called from <c>OnWobbleFrame</c> with the
    /// frame's clamped dt and the smoothed horizontal velocity it already keeps; this adds the
    /// vertical half, because a toss straight up or down is still a toss.
    /// </summary>
    private void NoteTossFrame(double dt, double vx)
    {
        try
        {
            _toss.Frame(Top, dt, vx, WobbleVelKeep, DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] toss frame failed");
        }
    }

    // ---------------------------------------------------------------- put down

    /// <summary>A real drag just ended, after the clamp and the placement save.</summary>
    private void OnPutDown()
    {
        try
        {
            bool flung = _toss.PutDown(DateTime.UtcNow);

            var ctx = new { zoneRow = ZoneRow(), flings20 = false, flings50 = false };

            if (flung)
            {
                int total = EmiState.NoteFling();
                EmiSfx.Thud();
                PlayLanding(thrown: true);
                var flingCtx = new { zoneRow = ctx.zoneRow, flings20 = total >= 20, flings50 = total >= 50 };
                // CAMPUS: "The dizzy chain always runs first." The line waits for it.
                PlayChain("dizzy", () => App.EmiDesk?.Fire("flung", flingCtx));
                return;
            }

            EmiSfx.Bump();
            PlayLanding(thrown: false);
            if (_player.IsLive) { App.EmiDesk?.Fire("dropped", ctx); return; }
            PlayChain(LandChain, () => App.EmiDesk?.Fire("dropped", ctx));
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] put down failed");
        }
    }

    /// <summary>
    /// The landing squash, from her FEET: the scale centre moves to the bottom of the body for the
    /// length of the animation and goes back to the middle when it ends, so the click squash (which
    /// wants the middle) is unaffected the rest of the time.
    /// </summary>
    private void PlayLanding(bool thrown)
    {
        try
        {
            if (_closingForGood) return;

            double bh = _bodyWidth * BodyAspect;
            // BodyRoot's RenderTransformOrigin is 0.55 of her height, so her feet are +0.45 bh.
            SquashScale.CenterY = bh * 0.45;

            var dur = TimeSpan.FromMilliseconds(thrown ? ThudMs : LandMs);
            var into = new CubicEase { EasingMode = EasingMode.EaseOut };
            var back = new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 1, Springiness = 4 };

            var ax = new DoubleAnimationUsingKeyFrames { Duration = dur, FillBehavior = FillBehavior.Stop };
            var ay = new DoubleAnimationUsingKeyFrames { Duration = dur, FillBehavior = FillBehavior.Stop };
            if (thrown)
            {
                // Still stretched from the flight on the first frame, then the hard squash.
                ax.KeyFrames.Add(new EasingDoubleKeyFrame(0.95, KeyTime.FromPercent(0.20), into));
                ay.KeyFrames.Add(new EasingDoubleKeyFrame(1.06, KeyTime.FromPercent(0.20), into));
                ax.KeyFrames.Add(new EasingDoubleKeyFrame(ThudSquashX, KeyTime.FromPercent(0.55), into));
                ay.KeyFrames.Add(new EasingDoubleKeyFrame(ThudSquashY, KeyTime.FromPercent(0.55), into));
            }
            else
            {
                ax.KeyFrames.Add(new EasingDoubleKeyFrame(LandSquashX, KeyTime.FromPercent(0.40), into));
                ay.KeyFrames.Add(new EasingDoubleKeyFrame(LandSquashY, KeyTime.FromPercent(0.40), into));
            }
            ax.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromPercent(1.0), back));
            ay.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromPercent(1.0), back));
            ay.Completed += (_, _) =>
            {
                try { SquashScale.CenterY = 0; }
                catch { /* she is gone */ }
            };

            SquashScale.BeginAnimation(ScaleTransform.ScaleXProperty, ax);
            SquashScale.BeginAnimation(ScaleTransform.ScaleYProperty, ay);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] landing squash failed");
            try { SquashScale.CenterY = 0; } catch { /* she is gone */ }
        }
    }

    /// <summary>Which third of her monitor's work area she was left in: top, mid or bottom.
    /// The campus's <c>zoneRow</c>, which is what the spot lines key on.</summary>
    private string ZoneRow()
    {
        try
        {
            var body = BodyScreenRect;
            var centre = new System.Drawing.Point(
                (int)Math.Round(body.X + body.Width / 2),
                (int)Math.Round(body.Y + body.Height / 2));
            var work = System.Windows.Forms.Screen.FromPoint(centre).WorkingArea;
            return EmiTossRules.ZoneRow(centre.Y, work.Top, work.Height);
        }
        catch
        {
            return "mid";
        }
    }
}
