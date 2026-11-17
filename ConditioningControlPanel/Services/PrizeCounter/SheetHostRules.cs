using System;
using System.Collections.Generic;
using System.Windows;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.PrizeCounter;

/// <summary>
/// Pure rules for the host side of the counter sheet (shop frame lane, 2026-11-17): where the host's wallet
/// star sits in the page's own CSS pixels (so the page's stars fly to the real chip), what a header wallet
/// shows while the DEBUG shop preview runs, and the Disco hues handed to the page.
/// </summary>
internal static class SheetHostRules
{
    /// <summary>
    /// The wallet star's centre in the page's CSS pixels, given that centre in the sheet slot's own space
    /// (DIPs) and the page's inset inside the slot once it is down. WebView2 draws one CSS pixel per DIP
    /// (zoom 1, the sheet never zooms), so this is a plain shift. The answer is usually above the page
    /// (negative y): the chip sits in the bar over the sheet. Null for an unusable point.
    /// </summary>
    public static (int X, int Y)? WalletAt(Point starInSlot, double insetX, double insetTop)
    {
        if (!double.IsFinite(starInSlot.X) || !double.IsFinite(starInSlot.Y) || !double.IsFinite(insetX) || !double.IsFinite(insetTop)) return null;
        double x = starInSlot.X - insetX, y = starInSlot.Y - insetTop;
        if (Math.Abs(x) > 20000 || Math.Abs(y) > 20000) return null;
        return ((int)Math.Round(x), (int)Math.Round(y));
    }

    /// <summary>
    /// What a header wallet (the launcher chip, the panel's wallet) shows. Without the preview: the real
    /// balance, signed in only. With the DEBUG shop preview on: the preview's own wallet, signed in or not,
    /// marked as preview, and the real balance is never shown beside the pretend shop.
    /// </summary>
    public static (bool Show, int Value, bool PreviewMark) HeaderWallet(bool preview, bool signedIn, int real, int previewSp)
        => preview ? (true, Math.Max(0, previewSp), true)
           : (signedIn && real >= 0, Math.Max(0, real), false);

    public enum DoorPress { Open, Nudge }

    /// <summary>
    /// A wallet press on a door window (polish lane). The sheet is in the window's own tree, so any other window
    /// draws above it: while a modal holds that window (its HWND disabled by a dialog or a message box) or the
    /// thread (a WPF <c>ShowDialog</c>), the press never opens the sheet under it; the chip only nudges.
    /// </summary>
    public static DoorPress OnDoorPress(bool doorEnabled, bool threadModal)
        => doorEnabled && !threadModal ? DoorPress.Open : DoorPress.Nudge;

    /// <summary>The door's own non-modal floating cards (a passive startup card, "What moved") hide while the
    /// sheet is down and come back when it is up; anything else the window owns is left alone.</summary>
    public static bool HidesWhileDown(bool visible, bool floatingCard) => visible && floatingCard;

    /// <summary>The chip's nudge, in DIPs either side: none at motion Off or with Gentle visuals, half at Reduced.</summary>
    public static double NudgeDip(MotionLevel level, bool gentle)
        => gentle || level == MotionLevel.Off ? 0 : level == MotionLevel.Reduced ? 2 : 4;

    /// <summary>The DEBUG shop preview's visible mark beside a header wallet's number (plain literal, preview chrome
    /// only; the panel wallet's label says "Preview wallet"). Null outside the preview.</summary>
    public static string? PreviewMarkText(bool previewMark) => previewMark ? "Preview" : null;

    /// <summary>The Disco hues as the page's <c>init.disco</c>: "#rrggbb" strings, at most six; null for none.</summary>
    public static string[]? DiscoWire(IReadOnlyList<uint>? rgb)
    {
        if (rgb == null || rgb.Count == 0) return null;
        var n = Math.Min(6, rgb.Count);
        var o = new string[n];
        for (int i = 0; i < n; i++) o[i] = "#" + (rgb[i] & 0xFFFFFF).ToString("x6");
        return o;
    }
}
