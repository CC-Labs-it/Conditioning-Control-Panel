// WPF Services/Friends/FriendsLanding.cs on this head: the words a delivery or a send throws through
// FloatingWord (poke received with Emi's line, the sent beat, Say/Tell from outside the drawer), routed by
// the Core FriendsLandingRouter. The rules are Core LandingRules; this reads the world and draws.
// ponytail: the corner toast (FriendNotices), the knock card (win-knock-card), the Inbox rows and the
// friend-request notices are not on this head yet, so Knock / Inbox / Request* draw nothing (folded into
// win-knock-card and feat-friends-open-tables); WPF's poke toast over a game is dropped with them.
// The flavour-on word rides a watch knock's Go (WPF WatchFlavour) and lands with win-knock-card.
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Friends;

namespace ConditioningControlPanel.Avalonia.Platform;

internal static class FriendsLanding
{
    private static DispatcherTimer? _timer;
    private static FriendsLandingRouter? _router;
    private static readonly Sink TheSink = new();

    /// <summary>Test seams; the app reads the real windows and services.</summary>
    internal static Func<LandingWorld> World { get; set; } = ReadWorld;
    internal static Func<Window?> Anchor { get; set; } = PanelOrLauncher;
    internal static Func<Window?> ActiveGameWindow { get; set; } = ActiveGame;
    internal static Action<string, string> EmiSays { get; set; } = DeskSays;
    internal static Func<IFriendsService?> Service { get; set; } = () => FriendsHead.Service;
    internal static FriendsLandingRouter? Router => _router;

    /// <summary>WPF FriendsLanding.Start: once at startup; a light timer re-attaches and releases the hold.</summary>
    internal static void Start()
    {
        if (_timer != null) return;
        _timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromSeconds(3) };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
        Tick();
    }

    internal static void Stop()
    {
        try { _timer?.Stop(); } catch { /* shutting down */ }
        _timer = null;
        _router?.Dispose();
        _router = null;
    }

    internal static void Tick()
    {
        try
        {
            var current = Service();
            if (!ReferenceEquals(_router?.Service, current))
            {
                _router?.Dispose();
                // The server's clock: an invite's at / expires_at are server times (WPF :57-59).
                _router = current == null ? null : new FriendsLandingRouter(current, () => World(), () => ServerClock.UtcNow, TheSink);
            }
            _router?.Release();
        }
        catch (Exception ex) { Serilog.Log.Debug("[Friends] landing tick: {E}", ex.Message); }
    }

    /// <summary>WPF FriendsLanding.Tell/Say: a short word where the player is looking (the game, the panel,
    /// the launcher). <paramref name="fallback"/> is the caller's own window when none of those is up.</summary>
    internal static void Tell(string text, bool good, Window? fallback = null)
    {
        var anchor = ActiveGameWindow() ?? Anchor() ?? fallback;
        if (anchor != null) FloatingWord.Throw(anchor, text, pink: !good, small: true);
    }

    // ---------------------------------------------------------------- the world (WPF ReadWorld :87)

    private static IClassicDesktopStyleApplicationLifetime? Desktop =>
        global::Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;

    private static bool OnScreen(Window? w) => w is { IsVisible: true } && w.WindowState != WindowState.Minimized;

    private static Window? Shell => Desktop?.MainWindow as MainShellWindow;

    private static Window? Launcher => Desktop?.Windows.OfType<LauncherWindow>().FirstOrDefault();

    private static LandingWorld ReadWorld()
    {
        var session = CoreSession.IsSessionRunning;
        bool lockdown = false, program = false, strict = false;
        try { lockdown = LockdownService.Current?.IsActive == true; } catch { /* not built yet */ }
        try { program = session && App.Programs?.ActiveEnrollment != null; } catch { /* not built yet */ }
        try { strict = session && CoreSettings.Current.StrictLockEnabled; } catch { /* not loaded */ }
        return new LandingWorld(
            Lockdown: lockdown, StrictLock: strict, ProgramSession: program,
            PanelVisible: OnScreen(Shell), LauncherVisible: OnScreen(Launcher),
            GameHostActive: GameWindow.IsAnyOpen(), SessionRunning: session);
    }

    /// <summary>WPF Anchor: the panel, else the launcher, when on screen.</summary>
    private static Window? PanelOrLauncher()
    {
        if (Shell is { } s && OnScreen(s)) return s;
        return Launcher is { } l && OnScreen(l) ? l : null;
    }

    /// <summary>WPF ActiveGameWindow: the active window when it is a game host's, while one is up.</summary>
    private static Window? ActiveGame() =>
        GameWindow.IsAnyOpen() ? GameWindow.OpenWindowsForTests().FirstOrDefault(w => w.IsActive) : null;

    private static void DeskSays(string line, string face)
    {
        try
        {
            var desk = EmiDeskService.Instance;
            if (desk.IsOut && desk.Window != null) desk.Window.Say(line, face);
        }
        catch (Exception ex) { Serilog.Log.Debug("[Friends] emi say: {E}", ex.Message); }
    }

    /// <summary>The drawer's bell (WPF NoticesOn).</summary>
    private static bool NoticesOn
    {
        get { try { return CoreSettings.Current.FriendNotificationsEnabled; } catch { return true; } }
    }

    private static string Str(string key, string english)
    {
        try { var v = Loc.Get(key); return string.IsNullOrWhiteSpace(v) || v == key ? english : v; }
        catch { return english; }
    }

    // ---------------------------------------------------------------- the sink

    private sealed class Sink : ILandingSink
    {
        /// <summary>WPF Sink.Poke :223: Emi says it and the word floats over the panel, pink for a tease.</summary>
        public void Poke(InboxItem item, bool inGame)
        {
            var game = inGame ? ActiveGameWindow() : null;
            if (!LandingRules.PokeThrowsWord(NoticesOn, game != null)) return;
            var word = Str("friends_poke_" + item.PokeId, LandingRules.PokeFallback(item.PokeId));
            var anchor = Anchor();
            if (anchor == null) return;
            FriendsSeen.Shared.Seen(Service(), item.Id);
            EmiSays(string.Format(Str("friends_land_emi_poke", "{0} says {1}"), item.FromName, word),
                LandingRules.PokeFace(item.PokeId));
            FloatingWord.Throw(anchor, word, LandingRules.PokeIsPink(item.PokeId));
        }

        /// <summary>WPF Sink.SentBeat :366: "knock sent" / "watch sent" / "sent", small and mint.</summary>
        public void SentBeat(SendKind kind, Friend to)
        {
            var anchor = ActiveGameWindow() ?? Anchor();
            if (anchor == null) return;
            var (key, english) = LandingRules.SentWord(kind);
            FloatingWord.Throw(anchor, Str(key, english), pink: false, small: true);
        }

        public void Knock(InboxItem item, bool inGame) { }
        public void Inbox(InboxItem item) { }
        public void RequestRow(FriendRequest request) { }
        public void RequestAnnounce(FriendRequest request, bool inGame) { }
        public void RequestCue() { }
        public void RequestGone(string requestId) { }
        public void RequestsWaiting(IReadOnlyList<FriendRequest> waiting, bool announce, bool inGame) { }
    }
}
