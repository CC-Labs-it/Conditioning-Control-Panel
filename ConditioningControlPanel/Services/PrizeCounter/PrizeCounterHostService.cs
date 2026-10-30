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
        (account, revision, grants) => App.Ownership?.ApplySnapshot(account, revision, grants));

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
    /// (<c>wallet</c>, ...); <paramref name="tab"/> the tab to open on, null for the first.</summary>
    public static void Launch(string from = "wallet", string? tab = null)
    {
        if (_host != null) { _host.FocusWeb(); return; }
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
                OnProcessFailed = kind => { App.Logger?.Warning("PrizeCounter: process failed ({Kind}), closing", kind); OnUi(Close); },
            });
            _host.Show();
            if (_host.Window != null) _host.Window.Closed += (_, _) => Close();
            _host.FocusWeb();
            App.Logger?.Information("PrizeCounterHostService: launched from {From}", _from);
        }
        catch (Exception ex)
        {
            App.Logger?.Error(ex, "PrizeCounterHostService.Launch failed");
            Close();
        }
    }

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

    /// <summary>Every exit: the page's close, the title-bar X, panic, a dead process, app exit. Idempotent.</summary>
    public static void Close()
    {
        if (_disposing) return;
        _disposing = true;
        try
        {
            ++_generation;
            bool had = _host != null;
            try { _host?.Dispose(); } catch (Exception ex) { Diag.Swallowed(ex); }
            _host = null;
            _relay = null;
            _opener = null;
            if (had) App.Logger?.Information("PrizeCounterHostService: closed");
        }
        catch (Exception ex) { App.Logger?.Debug("PrizeCounterHostService.Close: {E}", ex.Message); }
        finally { _disposing = false; }
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
                OnUi(Close);
                break;
            case "wear":
                // An owned card's Wear on a Rain row: the counter steps aside (queued, never inside the
                // WebView2 callback) and the dressing room opens with that piece on. One window at a time.
                if (generation == _generation)
                    HandleWear(m, Companion.RainItemManifest.Shipped, key => Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        Close();
                        try { OpenWardrobe(key); } catch (Exception ex) { App.Logger?.Warning(ex, "PrizeCounter: wardrobe open failed"); }
                    })));
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
