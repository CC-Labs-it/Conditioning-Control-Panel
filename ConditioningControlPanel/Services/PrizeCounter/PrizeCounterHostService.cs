using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Newtonsoft.Json.Linq;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.BackRoom;

namespace ConditioningControlPanel.Services.PrizeCounter;

/// <summary>
/// Host for THE PRIZE COUNTER (<c>Resources/web/prizecounter</c>, the 7.2 shop): its own
/// <see cref="ChaosWebViewHost"/>, windowed and owned by MainWindow. The page speaks the seam in
/// its CONTRACT.md (<c>ready / request / reply / wallet / close / signin / log</c> out, <c>init</c> in);
/// every request goes through <see cref="PrizeCounterRelay"/>. Signed out, the page shows one "Sign in
/// to shop" button whose <c>signin</c> frame opens the panel's sign-in over the counter.
///
/// <para>Safety: panic closes it at once (<c>GameSurfaces</c> id <see cref="GameId"/>). With the
/// panic key on Escape, a first Escape in front of the counter is the page's back (its Esc ladder:
/// confirm card, preview, leave); a second inside 2 s is a full panic (PanicPolicy). The window
/// itself never fades, so Gentle visuals is only the page's to honour (<c>init.gentle</c>).</para>
/// </summary>
internal static class PrizeCounterHostService
{
    public const string ProductName = "Prize Counter";
    public const string StartUrl = "https://ccp.game/prizecounter/index.html";
    internal const string GameId = "prizecounter";

    /// <summary>Constant on purpose: the argument string must not vary per launch for one user-data folder.</summary>
    public const string BrowserArguments = "--autoplay-policy=no-user-gesture-required";

    /// <summary><c>init.lexicon</c> carries every key with this prefix in the current language.</summary>
    internal const string LexPrefix = "shop_";

    /// <summary>The page plays this as its own Escape unless the real key reached it too (host-escape.js).</summary>
    internal const string KeptEscapeType = "kept-escape";

    private static ChaosWebViewHost? _host;
    private static PrizeCounterRelay? _relay;
    private static string _from = "wallet";
    private static string? _tab;
    private static int _generation;
    private static bool _disposing;
    private static string? _opener;
    private static bool _signingIn;
    /// <summary>The games paused under this counter (contract 1): each hears the close once, with its reason.</summary>
    private static readonly List<Action<string>> _onClosed = new();

    /// <summary>Why the counter went, as an opener under it hears it: <c>back</c> returns to the game,
    /// <c>panic</c> and <c>app-exit</c> never hand focus back, <c>wear</c> hands it to Rain's dressing room.</summary>
    internal const string CloseBack = "back", ClosePanic = "panic", CloseWear = "wear", CloseAppExit = "app-exit", CloseFailed = "failed";

    /// <summary>The counter's tabs, in the server's order (CONTRACT "Server body").</summary>
    internal static readonly string[] Tabs = { "rain-clothes", "rain-hair", "kart", "chess", "profile", "desk", "prime", "backroom" };

    /// <summary>The game pages that may ask for the counter (contract 1, <c>open-prize-counter</c>).</summary>
    internal static readonly string[] GameDoors = { "glassroad", "piecebypiece", "backroom", "rain-wardrobe" };

    /// <summary>A door's tab as the page knows it: contract 1's <c>rain</c> is the clothes tab, any
    /// other unknown name is null (the page opens on its first tab).</summary>
    internal static string? CounterTab(string? tab)
    {
        if (string.IsNullOrWhiteSpace(tab)) return null;
        var t = tab.Trim().ToLowerInvariant();
        if (t == "rain") return "rain-clothes";
        return Array.IndexOf(Tabs, t) >= 0 ? t : null;
    }

    /// <summary>A game page's <c>{ type:'open-prize-counter', tab, from }</c> (contract 1). False for any
    /// other frame or an unknown door; the tab is normalised (<see cref="CounterTab"/>).</summary>
    internal static bool TryReadOpenRequest(JObject? m, out string from, out string? tab)
    {
        from = string.Empty; tab = null;
        if (m == null || m["type"]?.Type != JTokenType.String || (string?)m["type"] != "open-prize-counter") return false;
        var f = m["from"]?.Type == JTokenType.String ? ((string?)m["from"])?.Trim() : null;
        if (f == null || Array.IndexOf(GameDoors, f) < 0) return false;
        from = f;
        tab = CounterTab(m["tab"]?.Type == JTokenType.String ? (string?)m["tab"] : null);
        return true;
    }

    /// <summary>An open counter asked again from another door: it reloads only when that door names a
    /// different tab (a door with no tab just brings it forward).</summary>
    internal static bool ReopensOnTab(string? current, string? wanted)
        => wanted != null && !string.Equals(current, wanted, StringComparison.Ordinal);

    /// <summary>The page's own <c>close {reason}</c>: only <c>panic</c> is kept, anything else is back.</summary>
    internal static string PageCloseReason(string? reason) => reason == ClosePanic ? ClosePanic : CloseBack;

    /// <summary>The panel's account sign-in, centred over the counter (the launcher's and the Back
    /// Room's flow). Settable so a test can answer without a dialog.</summary>
    internal static Action<Window?> RequestSignIn { get; set; } = owner =>
        // MainWindowRef, not Application.Current.MainWindow: that can be the launcher or null in the tray.
        (App.MainWindowRef ?? Application.Current?.MainWindow as MainWindow)?.OpenUnifiedLoginDialog(owner);

    /// <summary>True when somebody is signed in now who is not the account the counter is pinned to:
    /// the counter must pin them and load again (a signed-out counter has no pin).</summary>
    internal static bool SignedInSince(string? pinned, string? now)
        => !string.IsNullOrEmpty(now) && !string.Equals(pinned, now, StringComparison.Ordinal);

    private static PrizeCounterRelay NewRelay() => new(null,
        () => BackRoomApi.AppIdentity() is { } id && id.UnifiedId == _opener ? id : null,
        () => App.Settings?.Current?.OfflineMode == true,
        AdoptOnUi,
        (account, revision, grants) => App.Ownership?.ApplySnapshot(account, revision, grants))
    {
        // Feature day log: one e_shop per buy receipt (server EVENT_KEYS whitelists it, lane L6).
        BuyLanded = () => SeasonRecapService.TrackFeature(Models.SeasonFeatureKeys.Shop),
    };

    /// <summary>Rain's prices from the last shop-state this run (grant -> Sparkles), for the wardrobe's
    /// badges. Empty until the counter has been opened once; the server stays the price authority.</summary>
    private static IReadOnlyDictionary<string, int> _rainPrices = new Dictionary<string, int>(StringComparer.Ordinal);
    internal static IReadOnlyDictionary<string, int>? RainPrices => _rainPrices.Count > 0 ? _rainPrices : null;

    /// <summary>The Wear door's far end: Rain's dressing room with one item tried on. Settable for tests.</summary>
    internal static Func<string, bool> OpenWardrobe { get; set; } = key => Companion.RainWardrobeHostService.Open(tryOn: key);

    /// <summary>Startup (App): the wardrobe's "Open counter" door and its price badges come from here.</summary>
    internal static void WireRainWardrobe()
    {
        Companion.RainWardrobeHostService.CounterLauncher = (from, tab) => Launch(from, tab);
        Companion.RainWardrobeHostService.CounterPrices = () => RainPrices;
    }

    /// <summary>A counter row id -> the wardrobe key it wears (<c>slot:pageId</c>), through the row's grant
    /// <c>cos.&lt;id&gt;</c> in items.json. Null = not a Rain item the wardrobe knows: refused.</summary>
    internal static string? WearKey(string? rowId, Companion.RainItemManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(rowId) || !rowId.StartsWith("rain.", StringComparison.Ordinal)) return null;
        var grant = "cos." + rowId;
        return manifest.Items.FirstOrDefault(i => string.Equals(i.Grant, grant, StringComparison.Ordinal))?.Id;
    }

    /// <summary>The page's <c>wear {id}</c> frame: a known Rain row hands its key to <paramref name="wear"/>.</summary>
    internal static bool HandleWear(JObject m, Companion.RainItemManifest manifest, Action<string> wear)
    {
        var id = m["id"]?.Type == JTokenType.String ? (string?)m["id"] : null;
        var key = WearKey(id, manifest);
        if (key == null) { App.Logger?.Information("PrizeCounter: wear {Id} refused (no Rain item)", id ?? "(none)"); return false; }
        wear(key);
        return true;
    }

    // ============================ Wear, per family (fix wave F1) ============================

    internal const string FamilyRain = "rain", FamilyDesk = "desk", FamilyChess = "chess", FamilyProfile = "profile", FamilyKart = "kart";

    /// <summary>The family a counter row's Wear belongs to, by its id, or null (Back Room prizes and
    /// anything unknown: nothing to wear, the page shows no Wear button for it).</summary>
    internal static string? WearFamily(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        bool P(string prefix) => id.StartsWith(prefix, StringComparison.Ordinal);
        if (P("rain.")) return FamilyRain;
        if (P("desk.hat.") || P("desk.toy.")) return FamilyDesk;
        if (P("chess.set.") || P("chess.banner.") || P("chess.insignia.") || P("prime.banner.")) return FamilyChess;
        if (P("profile.frame.") || P("profile.avatar.") || P("prime.frame.")) return FamilyProfile;
        if (P("kart.") || P("prime.paint.") || P("prime.trail.")) return FamilyKart;
        return null;
    }

    /// <summary><c>init.wears</c>: the families this host can put on from the counter. A kart is worn
    /// only in the garage, so only a counter opened from Glassroad lists it; elsewhere the page reads
    /// "Wear it in the garage" and the button only steps back.</summary>
    internal static string[] WearsFor(string? from) => from == "glassroad"
        ? new[] { FamilyRain, FamilyDesk, FamilyChess, FamilyProfile, FamilyKart }
        : new[] { FamilyRain, FamilyDesk, FamilyChess, FamilyProfile };

    /// <summary>The kart a Wear closed the counter for (reason <see cref="CloseWear"/>): the garage under
    /// it reads this in its close hook and selects it. Cleared at every launch.</summary>
    internal static string? WornItem { get; private set; }

    /// <summary>Each family's own picker write (local first, PUT behind). Settable for tests.</summary>
    internal static Func<string, bool> WearDesk { get; set; } = id => EmiDesk.DeskEquipSync.WearFromCounter(id);
    internal static Func<string, bool> WearChess { get; set; } = id => PieceByPiece.PieceByPieceHostService.WearFromCounter(id);
    internal static Func<string, bool> WearProfile { get; set; } = id => App.MainWindowRef?.WearProfileItemFromCounter(id) == true;

    /// <summary>
    /// The page's <c>wear {id}</c>, routed by family. Rain closes the counter for the dressing room; a
    /// kart from the garage closes back to it (<see cref="WornItem"/>); desk, chess and profile are
    /// worn in place and the page hears <c>worn {id, ok}</c>. UI thread. Answers what it did.
    /// </summary>
    internal static string RouteWear(string? id, string? from)
    {
        WornItem = null;
        switch (WearFamily(id))
        {
            case FamilyRain:
                return HandleWear(new JObject { ["id"] = id }, Companion.RainItemManifest.Shipped, key =>
                {
                    CloseFor(CloseWear);
                    try { OpenWardrobe(key); } catch (Exception ex) { App.Logger?.Warning(ex, "PrizeCounter: wardrobe open failed"); }
                }) ? FamilyRain : "refused";
            case FamilyKart:
                if (from != "glassroad") { App.Logger?.Information("PrizeCounter: wear {Id} waits for the garage", id); return "garage"; }
                WornItem = id;
                CloseFor(CloseWear);
                return FamilyKart;
            case FamilyDesk: return WornInPlace(id!, WearDesk, FamilyDesk);
            case FamilyChess: return WornInPlace(id!, WearChess, FamilyChess);
            case FamilyProfile: return WornInPlace(id!, WearProfile, FamilyProfile);
            default:
                App.Logger?.Information("PrizeCounter: wear {Id} refused (nothing to wear)", id ?? "(none)");
                return "refused";
        }
    }

    private static string WornInPlace(string id, Func<string, bool> wear, string family)
    {
        bool ok;
        try { ok = wear(id); }
        catch (Exception ex) { App.Logger?.Warning(ex, "PrizeCounter: wear {Id} failed", id); ok = false; }
        App.Logger?.Information("PrizeCounter: wear {Id} ({Family}) {Result}", id, family, ok ? "on" : "refused");
        try { _host?.Post(new { type = "worn", id, ok }); } catch (Exception ex) { Diag.Swallowed(ex); }
        return ok ? family : "refused";
    }

    // ============================ the header wallet at boot (fix wave F1) ============================

    private static string? _walletReadFor;
    private static DateTime _walletReadAt;

    /// <summary>A shop-state read is due for this account: once per account, again after 10 minutes.</summary>
    internal static bool WalletReadDue(string? account, string? lastAccount, DateTime lastAt, DateTime now)
        => !string.IsNullOrEmpty(account)
           && (!string.Equals(account, lastAccount, StringComparison.Ordinal) || now - lastAt >= TimeSpan.FromMinutes(10));

    /// <summary>
    /// The header Sparkle wallet read 0 after boot until the counter opened: the profile read carries
    /// no <c>skill_points</c> and a fresh profile's first sync waits. So when the profile lands, one
    /// <c>shop-state</c> read through the counter's own relay: its <c>sp</c> is a snapshot and may only
    /// RAISE the wallet (<see cref="Prizes.V2WalletAdoption"/>), its <c>prizes</c> block fills the grants.
    /// Fire and forget, never throws, nothing while offline or signed out.
    /// </summary>
    public static void RefreshWalletInBackground()
    {
        try
        {
            var id = BackRoomApi.AppIdentity();
            if (id == null || App.Settings?.Current?.OfflineMode == true) return;
            var account = id.Value.UnifiedId;
            var now = DateTime.UtcNow;
            if (!WalletReadDue(account, _walletReadFor, _walletReadAt, now)) return;
            _walletReadFor = account;
            _walletReadAt = now;
            var relay = new PrizeCounterRelay(null,
                () => BackRoomApi.AppIdentity() is { } i && i.UnifiedId == account ? i : null,
                () => App.Settings?.Current?.OfflineMode == true,
                AdoptOnUi,
                (acc, revision, grants) => App.Ownership?.ApplySnapshot(acc, revision, grants));
            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    var reply = await relay.RelayAsync(PrizeCounterRelay.OpState, null, null).ConfigureAwait(false);
                    // A read that did not land (boot before the network, busy) leaves the next ProfileLoaded free to try.
                    if (reply?["ok"]?.Type != JTokenType.Boolean || !(bool)reply["ok"]!) { _walletReadFor = null; return; }
                    var prices = RainPricesFrom(reply);
                    if (prices.Count > 0) _rainPrices = prices;
                }
                catch (Exception ex) { _walletReadFor = null; App.Logger?.Debug("PrizeCounter: wallet read failed: {E}", ex.Message); }
            });
        }
        catch (Exception ex) { App.Logger?.Debug("PrizeCounter: wallet read skipped: {E}", ex.Message); }
    }

    /// <summary>Every <c>cos.rain.*</c> grant's price in one shop-state body (or a refusal carrying <c>state</c>).</summary>
    internal static Dictionary<string, int> RainPricesFrom(JObject? body)
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        var state = body?["state"] as JObject ?? body;
        if (state?["tabs"] is not JArray tabs) return map;
        foreach (var row in tabs.SelectTokens("[*].shelves[*].rows[*]").OfType<JObject>())
        {
            if (row["priceSp"] is not JValue { Type: JTokenType.Integer } price || row["grants"] is not JArray grants) continue;
            foreach (var g in grants.Values<string>())
                if (g != null && g.StartsWith("cos.rain.", StringComparison.Ordinal)) map[g] = price.Value<int>();
        }
        return map;
    }

    public static bool IsActive => _host != null;
    public static bool IsInFront => _host?.IsForeground == true;
    public static bool IsReady => _host?.IsReady == true;

    /// <summary>Open the counter, or bring it forward. <paramref name="from"/> is the door
    /// (<c>wallet</c>, ...); <paramref name="tab"/> the tab to open on, null for the first.
    /// <paramref name="onClosed"/> is a game paused under it (contract 1): it hears the close once,
    /// with its reason (<see cref="CloseBack"/> and the rest), and re-reads its grants then.
    /// An open counter asked from another door on another tab reloads on that tab.</summary>
    public static void Launch(string from = "wallet", string? tab = null, Action<string>? onClosed = null)
    {
        tab = CounterTab(tab);
        if (_host == null) WornItem = null;
        if (onClosed != null) _onClosed.Add(onClosed);
        if (_host != null)
        {
            if (ReopensOnTab(_tab, tab))
            {
                _tab = tab;
                _from = string.IsNullOrWhiteSpace(from) ? _from : from;
                Reload("tab " + tab);
            }
            _host?.FocusWeb();
            return;
        }
        try
        {
            _from = string.IsNullOrWhiteSpace(from) ? "wallet" : from;
            _tab = tab;
            var generation = ++_generation;
            // The identity pin: the counter speaks only for the account that opened it. A sign-out or
            // a switch under a live counter answers signed_out instead of moving another wallet. Only
            // the page's own "Sign in to shop" moves the pin (SignInFromPage), and it reloads the page.
            _opener = BackRoomApi.AppIdentity()?.UnifiedId;
            _relay = NewRelay();

            var webRoot = Path.Combine(AppContext.BaseDirectory, "Resources", "web");
            var mappings = new List<(string, string, CoreWebView2HostResourceAccessKind)>
            {
                // The page, its scene and the shared vendored three.js. Deny: nothing reads it cross-origin.
                ("ccp.game", webRoot, CoreWebView2HostResourceAccessKind.Deny),
                ChaosWebViewHost.ContentMapping(),
            };
            _host = new ChaosWebViewHost(new ChaosWebViewHost.Options
            {
                StartUrl = StartUrl,
                PrimaryHost = "ccp.game",
                Mappings = mappings,
                UserDataFolderName = "browser_data_prizecounter",
                InputEnabled = true,
                IsGame = true,   // the startup ladder goes quiet while it is up
                StartFullscreen = false,
                OwnedByMainWindow = true,
                CenterOnMainWindow = true,
                WindowTitle = ProductName,
                LogTag = "PrizeCounter",
                ExtraBrowserArguments = BrowserArguments,
                OnReady = () => Post(BuildInit()),
                OnMessage = m => OnPageMessage(m, generation),
                OnProcessFailed = kind => { App.Logger?.Warning("PrizeCounter: process failed ({Kind}), closing", kind); OnUi(() => CloseFor(CloseFailed)); },
            });
            _host.Show();
            if (_host.Window != null) _host.Window.Closed += (_, _) => Close();
            _host.FocusWeb();
            App.Logger?.Information("PrizeCounterHostService: launched from {From} on {Tab}", _from, _tab ?? "(first)");
        }
        catch (Exception ex)
        {
            App.Logger?.Error(ex, "PrizeCounterHostService.Launch failed");
            CloseFor(CloseFailed);
        }
    }

    /// <summary>The page again in the same window, with a fresh init (a new tab, a new account).</summary>
    private static void Reload(string why)
    {
        if (_host == null) return;
        _relay = NewRelay();
        var generation = ++_generation;
        try
        {
            _host.NavigatePage(StartUrl, () => Post(BuildInit()), m => OnPageMessage(m, generation));
            App.Logger?.Information("PrizeCounterHostService: reloaded ({Why})", why);
        }
        catch (Exception ex)
        {
            App.Logger?.Warning(ex, "PrizeCounter: reload failed, closing");
            CloseFor(CloseFailed);
        }
    }

    /// <summary>Raise an open counter over the game it was opened from (never opens one).</summary>
    public static void BringForward() => _host?.FocusWeb();

    /// <summary>Hand the page an Escape the panel kept as its back (PanicPolicy.KeptEscapeGoesTo).</summary>
    public static void PostKeptEscape()
    {
        try
        {
            _host?.Post(new { type = KeptEscapeType });
            _host?.FocusWeb();
        }
        catch (Exception ex) { App.Logger?.Debug("PrizeCounter: kept Escape post failed: {E}", ex.Message); }
    }

    /// <summary>Every exit: the page's close, the title-bar X, a dead process. Idempotent. The reason is
    /// <see cref="CloseBack"/> unless a <see cref="CloseFor"/> named another one.</summary>
    public static void Close() => CloseFor(CloseBack);

    /// <summary>Close with a reason the games under it hear (panic: GameSurfaces; app exit: App).</summary>
    public static void CloseFor(string reason)
    {
        if (_disposing) return;
        _disposing = true;
        List<Action<string>> openers;
        try
        {
            ++_generation;
            bool had = _host != null;
            try { _host?.Dispose(); } catch (Exception ex) { Diag.Swallowed(ex); }
            _host = null;
            _relay = null;
            _opener = null;
            if (had) App.Logger?.Information("PrizeCounterHostService: closed ({Reason})", reason);
        }
        catch (Exception ex) { App.Logger?.Debug("PrizeCounterHostService.Close: {E}", ex.Message); }
        finally
        {
            // Snapshotted before the callbacks run: one may open the counter again (a fresh list).
            openers = new List<Action<string>>(_onClosed);
            _onClosed.Clear();
            _disposing = false;
        }
        NotifyOpeners(openers, reason);
    }

    /// <summary>Each game under the counter hears the close once; one that throws cannot starve the rest.</summary>
    internal static void NotifyOpeners(IEnumerable<Action<string>> openers, string reason)
    {
        foreach (var opener in openers)
        {
            try { opener(reason); }
            catch (Exception ex) { App.Logger?.Warning("PrizeCounter: an opener's close hook threw: {E}", ex.Message); }
        }
    }

    private static void OnPageMessage(JObject m, int generation)
    {
        switch ((string?)m["type"])
        {
            case "request":
                var relay = _relay;
                var reqId = (string?)m["reqId"];
                if (relay == null || string.IsNullOrEmpty(reqId)) return;
                var op = (string?)m["op"];
                var body = m["body"] as JObject;
                var idem = m["idem"]?.Type == JTokenType.String ? (string?)m["idem"] : null;
                _ = System.Threading.Tasks.Task.Run(async () =>
                {
                    var reply = await relay.RelayAsync(op, body, idem).ConfigureAwait(false);
                    var prices = RainPricesFrom(reply);
                    if (prices.Count > 0) _rainPrices = prices;
                    OnUi(() => { if (generation == _generation) _host?.Post(new { type = "reply", reqId, body = reply }); });
                });
                break;
            case "wallet":
                if (m["sp"] is JValue { Type: JTokenType.Integer } sp && _relay?.HandleWalletFrame(sp.Value<int>()) != true)
                    App.Logger?.Debug("PrizeCounter: wallet frame {Sp} names no receipt, ignored", sp);
                break;
            case "close":
                App.Logger?.Information("PrizeCounter: page closed ({Reason})", (string?)m["reason"] ?? "back");
                var why = PageCloseReason((string?)m["reason"]);
                OnUi(() => CloseFor(why));
                break;
            case "wear":
                // An owned card's Wear, routed by family (RouteWear). Queued, never inside the WebView2
                // callback: a Rain row closes the counter for the dressing room, one window at a time.
                if (generation == _generation)
                {
                    var wearId = m["id"]?.Type == JTokenType.String ? (string?)m["id"] : null;
                    var from = _from;
                    Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (generation != _generation) return;
                        try { RouteWear(wearId, from); }
                        catch (Exception ex) { App.Logger?.Warning(ex, "PrizeCounter: wear failed"); }
                    }));
                }
                break;
            case "signin":
                // Queued, never inside the WebView2 message callback: the dialog runs its own loop.
                if (generation == _generation) Application.Current?.Dispatcher.BeginInvoke(new Action(SignInFromPage));
                break;
        }
    }

    /// <summary>The page's "Sign in to shop" (CONTRACT "Host seam" signIn, owner 2026-10-29). Opens the
    /// sign-in over the counter; once somebody is signed in the counter pins that account and reloads the
    /// page in place, so a fresh init and shop-state follow. Cancelled leaves everything as it was.</summary>
    private static void SignInFromPage()
    {
        if (_signingIn || _host == null) return;
        if (!SignedInSince(_opener, BackRoomApi.AppIdentity()?.UnifiedId))
        {
            _signingIn = true;
            try { RequestSignIn(_host.Window); }
            catch (Exception ex) { Diag.Swallowed(ex, "prizecounter sign-in"); }
            finally { _signingIn = false; }
        }
        var now = BackRoomApi.AppIdentity()?.UnifiedId;
        // Cancelled, or panic closed the counter while the dialog was up.
        if (_host == null || !SignedInSince(_opener, now)) return;
        _opener = now;
        _relay = NewRelay();
        var generation = ++_generation;
        try
        {
            _host.NavigatePage(StartUrl, () => Post(BuildInit()), m => OnPageMessage(m, generation));
            _host.FocusWeb();
            App.Logger?.Information("PrizeCounterHostService: signed in, counter reloaded");
        }
        catch (Exception ex)
        {
            App.Logger?.Warning(ex, "PrizeCounter: reload after sign-in failed, closing");
            Close();
        }
    }

    /// <summary>The <c>init</c> frame (CONTRACT "Host seam"). Never the token.</summary>
    private static object BuildInit()
    {
        var s = App.Settings?.Current;
        var id = BackRoomApi.AppIdentity();
        bool osAnim = true;
        try { osAnim = SystemParameters.ClientAreaAnimation; } catch { }
        bool gentle = Safety.GentleVisuals.On;
        return new
        {
            type = "init",
            protocol = 1,
            identity = id == null ? null : new { unifiedId = id.Value.UnifiedId },
            sp = s?.SkillPoints ?? 0,
            locale = LocalizationManager.Instance.CurrentLanguage,
            lexicon = LocalizationManager.Instance.KeysWithPrefix(LexPrefix)
                .Distinct(StringComparer.Ordinal).ToDictionary(k => k, k => Loc.Get(k), StringComparer.Ordinal),
            motion = MotionWire(s?.MotionLevel ?? Models.MotionLevel.Full, gentle),
            gentle,
            reducedMotion = !osAnim,
            tab = _tab,
            from = _from,
            wears = WearsFor(_from),
            platform = "desktop",
        };
    }

    /// <summary>The page's three levels. Gentle visuals caps motion at less, as it does for UI motion.</summary>
    internal static string MotionWire(Models.MotionLevel m, bool gentle) => m switch
    {
        Models.MotionLevel.Off => "off",
        Models.MotionLevel.Reduced => "less",
        _ => gentle ? "less" : "full",
    };

    /// <summary>The account is checked AGAIN inside the marshalled write: the player may be somebody
    /// else by then (the shape App's V2Purchase wiring uses).</summary>
    private static void AdoptOnUi(string account, bool fromBuy, int sp) => OnUi(() =>
    {
        if (App.Settings?.Current is not { } s) return;
        var same = string.Equals(BackRoomApi.AppIdentity()?.UnifiedId, account, StringComparison.Ordinal);
        if (!Prizes.V2WalletAdoption.Decide(same, fromBuy, sp, s.SkillPoints, out var next)) return;
        s.SkillPoints = next;
        App.Settings.Save();
    });

    private static void Post(object msg) => OnUi(() => _host?.Post(msg));

    private static void OnUi(Action a)
    {
        var d = Application.Current?.Dispatcher;
        if (d == null || d.CheckAccess()) { try { a(); } catch (Exception ex) { Diag.Swallowed(ex); } return; }
        d.BeginInvoke(new Action(() => { try { a(); } catch (Exception ex) { Diag.Swallowed(ex); } }));
    }
}
