using System;
using System.Windows;

namespace ConditioningControlPanel.Services.PrizeCounter;

/// <summary>
/// The 2D counter SHEET (owner 2026-11-17): a wallet door drops the counter down under its own wallet
/// bar instead of opening the 3D counter window. Pure rules, testable without a window: which door gets
/// the sheet, which window it hangs from, where it sits, and what a wallet press does while it is up.
/// The host wiring is <c>PrizeCounterHostService.Sheet.cs</c> and <c>ChaosWebViewHost.Sheet.cs</c>.
/// </summary>
internal static class CounterSheetRules
{
    /// <summary>The sheet page; the game doors keep <see cref="PrizeCounterHostService.StartUrl"/>.</summary>
    public const string SheetUrl = "https://ccp.game/prizecounter/sheet.html";

    /// <summary>The launcher's wallet chip; the panel's wallet and EMI's options both say <c>wallet</c>.</summary>
    public const string FromLauncher = "launcher", FromWallet = "wallet";

    /// <summary>Space left under the sheet, so the door window's bottom edge still shows (DIPs).</summary>
    public const double BottomInsetDip = 8;

    /// <summary>Smaller than this and the door window has no room for a sheet: the counter window opens instead (DIPs).</summary>
    public const double MinWidthDip = 360, MinHeightDip = 240;

    /// <summary>A wallet press this soon after the press-down that closed the sheet is that same click.</summary>
    public static readonly TimeSpan PressWindow = TimeSpan.FromMilliseconds(700);

    /// <summary>The wallet doors get the sheet. A game door (Back Room, Racing Thoughts, chess, Rain's
    /// wardrobe), or any door with a game paused under it (<paramref name="hasOpener"/>), keeps the window.</summary>
    public static bool GetsSheet(string? from, bool hasOpener)
        => !hasOpener && (from == FromLauncher || from == FromWallet);

    public enum Door { None, Launcher, Panel }

    /// <summary>The window the sheet hangs from. The launcher chip prefers the launcher; the panel's wallet
    /// (and EMI's options, which say <c>wallet</c> too) prefers the panel. Neither on screen: no sheet.</summary>
    public static Door PickDoor(string? from, bool launcherUp, bool panelUp)
    {
        if (!GetsSheet(from, hasOpener: false)) return Door.None;
        if (from == FromLauncher) return launcherUp ? Door.Launcher : panelUp ? Door.Panel : Door.None;
        return panelUp ? Door.Panel : launcherUp ? Door.Launcher : Door.None;
    }

    /// <summary>
    /// The sheet's frame in screen PIXELS: the door window's client left and right edges, from the bottom
    /// of its wallet bar down to <paramref name="bottomInset"/> above its bottom edge, clamped to the
    /// monitor (a maximized borderless window overhangs it). Null when that leaves less than the minimum,
    /// or the measures make no sense: the caller opens the counter window instead.
    /// </summary>
    public static Int32Rect? Bounds(Rect client, double barBottom, Rect monitor,
        double bottomInset, double minWidth, double minHeight)
    {
        if (client.IsEmpty || !Finite(client) || client.Width <= 0 || client.Height <= 0 || !double.IsFinite(barBottom)) return null;
        double left = client.Left, right = client.Right, top = Math.Max(client.Top, barBottom), bottom = client.Bottom;
        if (!monitor.IsEmpty && Finite(monitor) && monitor.Width > 0 && monitor.Height > 0)
        {
            left = Math.Max(left, monitor.Left);
            right = Math.Min(right, monitor.Right);
            top = Math.Max(top, monitor.Top);
            bottom = Math.Min(bottom, monitor.Bottom);
        }
        bottom -= Math.Max(0, bottomInset);
        if (right - left < minWidth || bottom - top < minHeight) return null;
        int x = (int)Math.Round(left), y = (int)Math.Round(top);
        return new Int32Rect(x, y, (int)Math.Round(right) - x, (int)Math.Round(bottom) - y);
    }

    private static bool Finite(Rect r) => double.IsFinite(r.X) && double.IsFinite(r.Y) && double.IsFinite(r.Width) && double.IsFinite(r.Height);

    public enum Press { Open, Close, Swallow }

    /// <summary>
    /// A wallet press. With the sheet up it closes it (the second click). With the sheet down it opens,
    /// unless the press-down on this same chip is what just closed it: that press-down took focus off
    /// the sheet (closing it on deactivation) before its click arrived, and the click must not open it
    /// again. <paramref name="armedAt"/> is when a deactivation found the cursor on the door's chip.
    /// </summary>
    public static Press OnPress(bool sheetOpen, DateTime? armedAt, DateTime now)
    {
        if (sheetOpen) return Press.Close;
        if (armedAt is { } at && now >= at && now - at <= PressWindow) return Press.Swallow;
        return Press.Open;
    }
}
