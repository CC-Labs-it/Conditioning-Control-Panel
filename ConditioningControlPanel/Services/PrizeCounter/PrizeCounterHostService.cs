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
/// its CONTRACT.md (<c>ready / request / reply / wallet / close / log</c> out, <c>init</c> in); every
/// request goes through <see cref="PrizeCounterRelay"/>.
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
            // a switch under a live counter answers signed_out instead of moving another wallet.
            var opener = BackRoomApi.AppIdentity()?.UnifiedId;
            _relay = new PrizeCounterRelay(null,
                () => BackRoomApi.AppIdentity() is { } id && id.UnifiedId == opener ? id : null,
                () => App.Settings?.Current?.OfflineMode == true,
                AdoptOnUi,
                (account, revision, grants) => App.Ownership?.ApplySnapshot(account, revision, grants));

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
