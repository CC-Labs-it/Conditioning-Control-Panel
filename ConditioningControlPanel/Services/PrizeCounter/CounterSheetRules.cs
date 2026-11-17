using System;
using System.Windows;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.PrizeCounter;

/// <summary>
/// The 2D counter SHEET (owner 2026-11-17): a wallet door slides the counter down INSIDE its own window,
/// from under the wallet bar, instead of opening the 3D counter window. Pure rules, testable without a
/// window: which door gets the sheet, which window it lives in, its frame inside that window, the
/// wallet's second press, and the slide. The view is <c>Controls/CounterSheetView.cs</c>.
/// </summary>
internal static class CounterSheetRules
{
    /// <summary>The sheet page; the game doors keep <see cref="PrizeCounterHostService.StartUrl"/>.</summary>
    public const string SheetUrl = "https://ccp.game/prizecounter/sheet.html";

    /// <summary>The launcher's wallet chip; the panel's wallet and EMI's options both say <c>wallet</c>.</summary>
    public const string FromLauncher = "launcher", FromWallet = "wallet";

    /// <summary>Gap between the sheet and the window's sides and bottom (DIPs): the sheet reads as a tab
    /// inside the launcher or the panel, which stays visible round it under the scrim (owner 2026-11-17).</summary>
    public const double SideInsetDip = 44, BottomInsetDip = 40;

    /// <summary>Gap between the wallet bar and the sheet's top edge once it is down (DIPs).</summary>
    public const double GapUnderBarDip = 10;

    /// <summary>The sheet's frame: corner radius, gold edge, and the gap between the edge and the page
    /// (DIPs). The page's HWND gets a window region rounded at <see cref="InnerRadiusDip"/>, concentric
    /// with the frame, so no square corner shows past the curve; the page's own gold bottom line then
    /// sits on the frame's.</summary>
    public const double CornerRadiusDip = 14, BorderDip = 2, PaddingDip = 0;
    public const double InnerRadiusDip = CornerRadiusDip - BorderDip - PaddingDip;

    /// <summary>Smaller than this and the door window has no room for a sheet: the counter window opens instead (DIPs).</summary>
    public const double MinWidthDip = 360, MinHeightDip = 240;

    /// <summary>The slide at full motion, and at reduced motion (half).</summary>
    public const int SlideMs = 280, ReducedSlideMs = 140;

    /// <summary>The wallet doors get the sheet. A game door (Back Room, Racing Thoughts, chess, Rain's
    /// wardrobe), or any door with a game paused under it (<paramref name="hasOpener"/>), keeps the window.</summary>
    public static bool GetsSheet(string? from, bool hasOpener)
        => !hasOpener && (from == FromLauncher || from == FromWallet);

    public enum Door { None, Launcher, Panel }

    /// <summary>The window the sheet lives in. The launcher chip prefers the launcher; the panel's wallet
    /// (and EMI's options, which say <c>wallet</c> too) prefers the panel. Neither on screen: no sheet.</summary>
    public static Door PickDoor(string? from, bool launcherUp, bool panelUp)
    {
        if (!GetsSheet(from, hasOpener: false)) return Door.None;
        if (from == FromLauncher) return launcherUp ? Door.Launcher : panelUp ? Door.Panel : Door.None;
        return panelUp ? Door.Panel : launcherUp ? Door.Launcher : Door.None;
    }

    /// <summary>
    /// The sheet's slot in the door window's own client space (DIPs; the sheet itself starts
    /// <see cref="GapUnderBarDip"/> lower): from the bottom of the wallet <paramref name="bar"/> down to <paramref name="bottomInset"/> above the window's bottom edge, from the
    /// bar's left edge (never closer than <paramref name="sideInset"/> to the window's) to
    /// <paramref name="sideInset"/> short of its right edge. Null when that leaves less than the minimum or
    /// the measures make no sense: the caller opens the counter window instead.
    /// </summary>
    public static Rect? Frame(Size client, Rect bar, double sideInset, double bottomInset, double minWidth, double minHeight)
    {
        if (!double.IsFinite(client.Width) || !double.IsFinite(client.Height) || client.Width <= 0 || client.Height <= 0) return null;
        if (bar.IsEmpty || !double.IsFinite(bar.Left) || !double.IsFinite(bar.Bottom)) return null;
        double left = Math.Max(Math.Max(0, sideInset), bar.Left);
        double right = client.Width - Math.Max(0, sideInset);
        double top = Math.Max(0, bar.Bottom);
        double bottom = client.Height - Math.Max(0, bottomInset);
        if (right - left < minWidth || bottom - top < minHeight) return null;
        return new Rect(left, top, right - left, bottom - top);
    }

    public enum Press { Open, Close }

    /// <summary>A wallet press: with the sheet up it closes it (the second click), otherwise it opens.</summary>
    public static Press OnPress(bool sheetOpen) => sheetOpen ? Press.Close : Press.Open;

    /// <summary>How long the sheet slides. Motion Off cuts, Reduced halves; Gentle visuals never slides
    /// (the page fades itself in, and the scrim fades: <see cref="ScrimMs"/>).</summary>
    public static int SlideDurationMs(MotionLevel level, bool gentle)
        => level == MotionLevel.Off || gentle ? 0 : level == MotionLevel.Reduced ? ReducedSlideMs : SlideMs;

    /// <summary>How long the scrim fades. Off cuts; Gentle keeps its 300 ms rise floor.</summary>
    public static int ScrimMs(MotionLevel level, bool gentle)
        => level == MotionLevel.Off ? 0 : gentle ? Safety.GentleVisuals.RiseMs(SlideMs, on: true) : SlideDurationMs(level, false);

    /// <summary>How long the gold edge takes to light up once the page has landed. Off cuts; Gentle
    /// keeps its 300 ms rise floor.</summary>
    public static int EdgeFadeMs(MotionLevel level, bool gentle)
        => level == MotionLevel.Off ? 0 : Safety.GentleVisuals.RiseMs(level == MotionLevel.Reduced ? 60 : 120, gentle);

    /// <summary>Ease out (cubic), clamped: fast off the bar, soft at the bottom.</summary>
    public static double EaseOut(double t)
    {
        t = Math.Clamp(double.IsFinite(t) ? t : 1, 0, 1);
        double u = 1 - t;
        return 1 - u * u * u;
    }

    /// <summary>How far above its open place the sheet sits at progress <paramref name="p"/> (0 closed,
    /// 1 open) in a slot <paramref name="height"/> tall: closed, it is wholly above the bar line.</summary>
    public static double SlideOffset(double p, double height)
    {
        p = Math.Clamp(double.IsFinite(p) ? p : 0, 0, 1);
        height = double.IsFinite(height) ? Math.Max(0, height) : 0;
        return (1 - p) * height;
    }
}
