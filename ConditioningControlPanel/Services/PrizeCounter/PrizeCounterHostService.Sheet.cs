using System;
using System.Windows;
using ConditioningControlPanel.Controls;

namespace ConditioningControlPanel.Services.PrizeCounter;

/// <summary>A window with a Sparkles wallet bar the counter sheet slides down under (the launcher, the panel).</summary>
internal interface ICounterSheetDoor
{
    /// <summary>The window's own sheet, in its visual tree under the wallet bar. Null = this window has none.</summary>
    CounterSheetView? CounterSheet { get; }
}

/// <summary>
/// The counter SHEET (owner 2026-11-17: "a Tab inside the window that smoothly comes down"): the same
/// relay, identity pin, init and page messages as the counter window, with
/// <see cref="CounterSheetRules.SheetUrl"/> and <c>init.sheet = true</c>, on the door window's own
/// <see cref="CounterSheetView"/>. It closes on the page's <c>close</c>, a click on the scrim, a second
/// wallet press and panic (same GameSurfaces entry); losing activation to another app does not close it.
/// </summary>
internal static partial class PrizeCounterHostService
{
    /// <summary>The open sheet is a wallet door's: <c>init.sheet = true</c>. A sheet over a game leaves it out,
    /// so the page draws its own wallet line (no wallet bar there), as CONTRACT.md's standalone page.</summary>
    private static bool _isSheet;
    private static string _startUrl = StartUrl;
    /// <summary>The open sheet's view (a wallet door's or a game window's), and whether it is over a game.</summary>
    private static CounterSheetView? _sheetView;
    private static bool _inGame;

    /// <summary>True while the open counter is a sheet (not the 3D window).</summary>
    public static bool IsSheetOpen => _host != null && _sheetView != null;

    /// <summary>What shows the open counter now.</summary>
    internal static CounterSheetRules.Surface OpenSurface
        => _host == null ? CounterSheetRules.Surface.None
           : _sheetView == null ? CounterSheetRules.Surface.Window
           : _inGame ? CounterSheetRules.Surface.GameSheet : CounterSheetRules.Surface.DoorSheet;

    /// <summary>The old 3D counter window for the game doors: DEBUG <c>CCP_COUNTER_3D=1</c> only. Release
    /// builds always open the sheet (the window stays as the fallback when a game window cannot carry one).</summary>
    internal static bool Force3D
    {
        get
        {
#if DEBUG
            return Environment.GetEnvironmentVariable("CCP_COUNTER_3D") == "1";
#else
            return false;
#endif
        }
    }

    /// <summary>A game door's own window, by door (settable for tests). Null = that game is not up.</summary>
    internal static Func<string?, ChaosWebViewHost?> GameHostOf { get; set; } = from => from switch
    {
        "backroom" => BackRoom.BackRoomHostService.CounterHost,
        "glassroad" => Glassroad.GlassroadHostService.CounterHost,
        "piecebypiece" => PieceByPiece.PieceByPieceHostService.CounterHost,
        "rain-wardrobe" => Companion.RainWardrobeHostService.CounterHost,
        _ => null,
    };

    /// <summary>The sheet inside this game door's window, or null (the counter window opens instead).</summary>
    private static GameCounterSheet? GameSheetFor(string? from)
    {
        try { return GameHostOf(from) is { } game ? GameCounterSheet.For(game, from!) : null; }
        catch (Exception ex) { App.Logger?.Warning("PrizeCounter: game sheet lookup failed: {E}", ex.Message); return null; }
    }

    /// <summary>The door's sheet for a wallet door now, or null (no sheet: the counter window opens).</summary>
    private static CounterSheetView? SheetDoorView(string from)
    {
        var launcher = Launcher.LauncherHost.WindowRef;
        var panel = App.MainWindowRef;
        bool launcherUp = launcher is ICounterSheetDoor && launcher.IsVisible && launcher.WindowState != WindowState.Minimized;
        bool panelUp = panel != null && panel.IsVisible && panel.WindowState != WindowState.Minimized && !panel.BuildingHiddenForBoot;
        Window? door = CounterSheetRules.PickDoor(from, launcherUp, panelUp) switch
        {
            CounterSheetRules.Door.Launcher => launcher,
            CounterSheetRules.Door.Panel => panel,
            _ => null,
        };
        return (door as ICounterSheetDoor)?.CounterSheet;
    }

    /// <summary>Open the sheet for this launch, or false (the caller opens the window). <paramref name="game"/>
    /// is the game window's sheet (its game goes still under the scrim), null for a wallet door's.</summary>
    private static bool TryLaunchSheet(CounterSheetView? view, int generation, GameCounterSheet? game)
    {
        if (view == null) return false;
        SheetCounterSurface? surface = null;
        surface = SheetCounterSurface.TryOpen(view, why => OnSheetWantsClose(surface, why));
        if (surface == null) return false;
        _isSheet = game == null;
        _sheetView = view;
        _inGame = game != null;
        _startUrl = CounterSheetRules.SheetUrl;
        _host = surface;
        game?.Freeze();
        // A door behind the user's other apps (EMI's options) comes forward first, so the sheet is never
        // left behind someone else's app.
        if (!view.DoorWindow.IsActive) { try { view.DoorWindow.Activate(); } catch (Exception ex) { Diag.Swallowed(ex); } }
        // A game window's sheet may be cold (its browser made on this press): the keys come to the page
        // once it answers, never left with the game under it.
        _host.NavigatePage(_startUrl, () => { Post(BuildInit()); if (game != null) OnUi(() => _host?.FocusWeb()); }, m => OnPageMessage(m, generation));
        _host.FocusWeb();
        App.Logger?.Information("PrizeCounterHostService: sheet down from {From} on {Tab}", _from, _tab ?? "(first)");
        return true;
    }

    /// <summary>The sheet asks to go (scrim, its door hid or closed, no room, a dead page). Queued, never
    /// inside the view's own handler; a sheet that is no longer the open counter is ignored.</summary>
    private static void OnSheetWantsClose(ICounterSurface? surface, string why)
    {
        if (surface == null || !ReferenceEquals(surface, _host)) return;
        App.Logger?.Information("PrizeCounter: sheet closing ({Why})", why);
        Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
        {
            if (ReferenceEquals(surface, _host)) CloseFor(CounterSheetRules.SheetCloseReason(why));
        }));
    }

    /// <summary>
    /// A wallet door's press while the sheet may be up (<see cref="CounterSheetRules.OnPress"/>). True when
    /// the press was handled here (the second press closed it); false = open.
    /// </summary>
    private static bool SheetPressHandled()
    {
        if (CounterSheetRules.OnPress(OpenSurface == CounterSheetRules.Surface.DoorSheet) != CounterSheetRules.Press.Close) return false;
        App.Logger?.Information("PrizeCounter: wallet pressed again, sheet closing");
        CloseFor(CloseBack);
        return true;
    }
}

/// <summary>The sheet as a <see cref="ICounterSurface"/>: one open of a door's <see cref="CounterSheetView"/>.</summary>
internal sealed class SheetCounterSurface : ICounterSurface
{
    private readonly CounterSheetView _view;
    private readonly int _session;

    private SheetCounterSurface(CounterSheetView view, int session) { _view = view; _session = session; }

    /// <summary>Slide the door's sheet down; null when it cannot open (no room, no WebView2 runtime).</summary>
    public static SheetCounterSurface? TryOpen(CounterSheetView view, Action<string> wantsClose)
    {
        int session = view.Open(wantsClose);
        return session == 0 ? null : new SheetCounterSurface(view, session);
    }

    public Window? Window => _view.DoorWindow;
    public bool IsReady => _view.IsPageReady(_session);
    public bool IsForeground => _view.IsForeground(_session);
    public void NavigatePage(string url, Action ready, Action<Newtonsoft.Json.Linq.JObject> message) => _view.Load(_session, url, ready, message);
    public void Post(object msg) => _view.Post(_session, msg);
    public void FocusWeb() => _view.FocusPage(_session);
    public void Close(bool animate) => _view.Close(_session, animate);
}
