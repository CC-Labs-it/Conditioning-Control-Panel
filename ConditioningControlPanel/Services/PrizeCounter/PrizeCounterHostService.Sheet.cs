using System;
using System.Windows;
using System.Windows.Interop;

namespace ConditioningControlPanel.Services.PrizeCounter;

/// <summary>A window with a Sparkles wallet bar the counter sheet can hang under (the launcher, the panel).</summary>
internal interface ICounterSheetDoor
{
    /// <summary>The bar the wallet sits in: the sheet starts at its bottom edge.</summary>
    FrameworkElement? CounterSheetBar { get; }

    /// <summary>The wallet chip itself: a press-down here is what a second click looks like.</summary>
    FrameworkElement? CounterSheetChip { get; }
}

/// <summary>
/// The counter SHEET (CounterSheetRules): the same host, relay, identity pin, init and page messages as
/// the counter window, with <see cref="CounterSheetRules.SheetUrl"/> and <c>init.sheet = true</c>, in a
/// borderless window owned by the door window under its wallet bar. It closes (reason back) when it loses
/// activation, when its owner hides or minimizes, on the page's <c>close</c>, on a second wallet click,
/// and on panic through the same GameSurfaces entry as the window.
/// </summary>
internal static partial class PrizeCounterHostService
{
    private static bool _isSheet;
    private static string _startUrl = StartUrl;
    private static Window? _sheetOwner;
    private static DateTime? _sheetPressArmedAt;

    /// <summary>True while the open counter is the sheet (not the 3D window).</summary>
    public static bool IsSheetOpen => _host != null && _isSheet;

    /// <summary>The door window for a wallet door now, or null (no sheet: the counter window opens).</summary>
    private static Window? SheetDoorWindow(string from)
    {
        var launcher = Launcher.LauncherHost.WindowRef;
        var panel = App.MainWindowRef;
        bool launcherUp = launcher is ICounterSheetDoor && launcher.IsVisible && launcher.WindowState != WindowState.Minimized;
        bool panelUp = panel != null && panel.IsVisible && panel.WindowState != WindowState.Minimized && !panel.BuildingHiddenForBoot;
        return CounterSheetRules.PickDoor(from, launcherUp, panelUp) switch
        {
            CounterSheetRules.Door.Launcher => launcher,
            CounterSheetRules.Door.Panel => panel,
            _ => null,
        };
    }

    /// <summary>The sheet's frame in screen pixels under <paramref name="owner"/>'s wallet bar, or null.</summary>
    internal static Int32Rect? MeasureSheet(Window owner)
    {
        try
        {
            if (owner is not ICounterSheetDoor door || owner.Content is not FrameworkElement root) return null;
            var bar = door.CounterSheetBar;
            if (bar == null || !bar.IsVisible || PresentationSource.FromVisual(bar) == null || PresentationSource.FromVisual(root) == null) return null;
            var tl = root.PointToScreen(new Point(0, 0));
            var br = root.PointToScreen(new Point(root.ActualWidth, root.ActualHeight));
            double barBottom = bar.PointToScreen(new Point(0, bar.ActualHeight)).Y;
            double scale = PresentationSource.FromVisual(owner)?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;
            if (scale <= 0) scale = 1.0;
            var hwnd = new WindowInteropHelper(owner).Handle;
            var screen = hwnd == IntPtr.Zero ? Rect.Empty : ScreenRect(System.Windows.Forms.Screen.FromHandle(hwnd).Bounds);
            return CounterSheetRules.Bounds(new Rect(tl, br), barBottom, screen,
                CounterSheetRules.BottomInsetDip * scale, CounterSheetRules.MinWidthDip * scale, CounterSheetRules.MinHeightDip * scale);
        }
        catch (Exception ex) { App.Logger?.Debug("PrizeCounter: sheet measure failed: {E}", ex.Message); return null; }
    }

    private static Rect ScreenRect(System.Drawing.Rectangle b) => new(b.X, b.Y, b.Width, b.Height);

    /// <summary>A press on the door's chip (cursor inside its screen rect) right as the sheet loses focus.</summary>
    private static bool CursorOnChip(Window? owner)
    {
        try
        {
            if (owner is not ICounterSheetDoor { CounterSheetChip: { IsVisible: true } chip } || PresentationSource.FromVisual(chip) == null) return false;
            var a = chip.PointToScreen(new Point(0, 0));
            var b = chip.PointToScreen(new Point(chip.ActualWidth, chip.ActualHeight));
            var p = System.Windows.Forms.Cursor.Position;
            return new Rect(a, b).Contains(new Point(p.X, p.Y));
        }
        catch (Exception ex) { Diag.Swallowed(ex); return false; }
    }

    /// <summary>The host's sheet callback: lost focus or lost its owner. The close is queued, never
    /// inside the window's own Deactivated; the sign-in dialog over the sheet is not a loss.</summary>
    private static void OnSheetLost(ChaosWebViewHost? host, string why)
    {
        if (host == null || !ReferenceEquals(host, _host)) return;
        if (why == "deactivated" && _signingIn) return;
        if (why == "deactivated" && CursorOnChip(_sheetOwner)) _sheetPressArmedAt = DateTime.UtcNow;
        App.Logger?.Information("PrizeCounter: sheet lost ({Why}), closing", why);
        Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
        {
            if (ReferenceEquals(host, _host)) CloseFor(CloseBack);
        }));
    }

    /// <summary>
    /// A wallet door's press while the sheet may be up (<see cref="CounterSheetRules.OnPress"/>). True when
    /// the press was handled here (closed, or swallowed as the click that already closed it); false = open.
    /// </summary>
    private static bool SheetPressHandled()
    {
        var armed = _sheetPressArmedAt;
        _sheetPressArmedAt = null;
        switch (CounterSheetRules.OnPress(IsSheetOpen, armed, DateTime.UtcNow))
        {
            case CounterSheetRules.Press.Close:
                App.Logger?.Information("PrizeCounter: wallet pressed again, sheet closing");
                CloseFor(CloseBack);
                return true;
            case CounterSheetRules.Press.Swallow:
                return true;
            default:
                return false;
        }
    }
}
