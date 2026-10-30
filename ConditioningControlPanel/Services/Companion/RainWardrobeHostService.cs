using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using ConditioningControlPanel.Localization;
using Microsoft.Web.WebView2.Core;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Companion;

/// <summary>
/// Rain's wardrobe on WPF (7.2 preview, lane R0 of handoff-1030/RAIN-V2-PLAN.md): the accepted three.js
/// fitting room (<c>Resources/web/rain-wardrobe</c>) in one windowed <see cref="ChaosWebViewHost"/>, which
/// attaches ScrolllerWebGate. Kept minimal on purpose: the dressing room (lanes R4/R5) replaces it.
///
/// <para>The host owns the look: the saved <c>AppSettings.RainLook</c> goes in as <c>?host=ccp&amp;look=</c>
/// and again in the <c>init</c> frame (applyMode:true, lane R5). Changes are a draft on the page; only
/// <c>rain-apply</c> reaches the tube, through <see cref="RainApplyController"/> (clamp, bake, commit).
/// Prize Counter: items the account does not own (<see cref="RainWardrobeCatalog"/> against
/// <c>App.Ownership</c> grants) go in as <c>?locked=</c>, show "In the Prize Counter" and cannot be
/// worn; a buy while the window is up posts <c>rain-locked</c>. Single items only (the page hides its
/// bundled looks). The stored look is clamped to the grants on open and on every store.</para>
///
/// <para>avatar.glb and pelvis-data.js never ship in the repo: <c>ccp.rain-assets</c> maps
/// <see cref="RainWardrobeAssets"/> (<c>CCP_RAIN_ASSETS_DIR</c>, else <c>&lt;user data&gt;/rain-assets</c>).
/// Missing files = one toast, no window. Panic closes it (GameSurfaces <c>rain-wardrobe</c>); it is not a
/// game, so Escape is the panic key's as everywhere else. Own browser profile, constant arguments.</para>
/// </summary>
internal static class RainWardrobeHostService
{
    internal const string PageHost = "ccp.game";
    internal const string AssetsHost = "ccp.rain-assets";
    internal const string PagePath = "rain-wardrobe/index.html";
    internal const string UserDataFolder = "browser_data_rainwardrobe";

    private static ChaosWebViewHost? _host;
    private static bool _disposing;
    private static RainApplyController? _apply;
    private static readonly RainSerialQueue BakeQueue = new();
    private static IReadOnlyList<string>? _clips;

    /// <summary>The Prize Counter door (lane R5): the counter host sets it when it lands on this
    /// branch (<c>PrizeCounterHostService.Launch(from, tab)</c>). Null = no counter in this build.</summary>
    internal static Action<string, string?>? CounterLauncher { get; set; }

    /// <summary>The counter's cached prices (grant or slot:id -> Sparkles); null or empty = no prices.</summary>
    internal static Func<IReadOnlyDictionary<string, int>?>? CounterPrices { get; set; }

    public static bool IsActive => _host != null || RainAssetInstallWindow.IsOpen;

    /// <summary>This run's big-asset folder.</summary>
    internal static string AssetsDir
        => RainWardrobeAssets.ResolveDir(Environment.GetEnvironmentVariable(RainWardrobeAssets.EnvVar), App.UserDataPath);

    /// <summary>The page URL, pure: host mode, the saved look (clamped, omitted when default), the
    /// locked items and the localised lock line. Never the review or QA studio.</summary>
    internal static string PageUrl(string? storedLook, IEnumerable<string>? grants, string lockNote)
    {
        var look = RainWardrobeCatalog.Clamp(storedLook, grants);
        var q = "host=ccp";
        if (look.Length > 0) q += "&look=" + Uri.EscapeDataString(look);
        var locked = RainWardrobeCatalog.LockedKeys(grants);
        if (locked.Count > 0) q += "&locked=" + Uri.EscapeDataString(string.Join(",", locked));
        if (!string.IsNullOrWhiteSpace(lockNote)) q += "&lockNote=" + Uri.EscapeDataString(lockNote);
        return $"https://{PageHost}/{PagePath}?{q}";
    }

    /// <summary>The door (tube menu while Rain plays). One live window, focused rather than reopened.</summary>
    public static void Open()
    {
        if (_host != null) { _host.FocusWeb(); return; }
        var dir = AssetsDir;
        var missing = RainWardrobeAssets.Missing(dir);
        var installer = RainAssetInstaller.ForApp();
        if (!installer.IsInstalled())
        {
            // Lane R12: the native install card, never a broken page; success reopens this door.
            App.Logger?.Information("RainWardrobe: not installed: [{Missing}] missing or an older pack in {Dir} (or set {Env})",
                string.Join(", ", missing), dir, RainWardrobeAssets.EnvVar);
            RainAssetInstallWindow.ShowFor(installer, () => RunOnUi(Open));
            return;
        }
        try
        {
            var settings = App.Settings?.Current;
            var grants = App.Ownership?.Grants;
            if (settings != null)
            {
                var clamped = RainWardrobeCatalog.Clamp(settings.RainLook, grants);
                if (!string.Equals(clamped, settings.RainLook, StringComparison.Ordinal)) { settings.RainLook = clamped; App.Settings!.Save(); }
            }
            var webRoot = Path.Combine(AppContext.BaseDirectory, "Resources", "web");
            _host = new ChaosWebViewHost(new ChaosWebViewHost.Options
            {
                StartUrl = PageUrl(settings?.RainLook, grants, Loc.Get("rain_wardrobe_locked")),
                PrimaryHost = PageHost,
                Mappings = new List<(string, string, CoreWebView2HostResourceAccessKind)>
                {
                    // The page and the shared three.js vendor tree; never read cross-origin.
                    (PageHost, webRoot, CoreWebView2HostResourceAccessKind.Deny),
                    // Rain's big files, fetched and imported cross-origin by the page.
                    (AssetsHost, dir, CoreWebView2HostResourceAccessKind.Allow),
                },
                UserDataFolderName = UserDataFolder,
                InputEnabled = true,
                StartFullscreen = false,
                OwnedByMainWindow = true,
                WindowedWidth = 1180,
                WindowedHeight = 760,
                CenterOnMainWindow = true,
                WindowTitle = Loc.Get("rain_wardrobe_title"),
                LogTag = "RainWardrobe",
                OnMessage = OnPageMessage,
                OnProcessFailed = kind =>
                {
                    App.Logger?.Warning("RainWardrobe: browser process failed ({Kind}) - closing", kind);
                    RunOnUi(Close);
                },
            });
            _apply = new RainApplyController(ApplyHooks(_host));
            _host.Show();
            if (_host.Window != null) _host.Window.Closed += (_, _) => Close();
            if (App.Ownership != null) { App.Ownership.OwnershipChanged -= OnOwnershipChanged; App.Ownership.OwnershipChanged += OnOwnershipChanged; }
            _host.FocusWeb();
            App.Logger?.Information("RainWardrobe: opened");
        }
        catch (Exception ex)
        {
            App.Logger?.Error(ex, "RainWardrobe: open failed");
            Close();
        }
    }

    /// <summary>The one funnel for every exit (title-bar X, panic, dead browser, shutdown). Idempotent.</summary>
    public static void Close()
    {
        if (_disposing) return;
        _disposing = true;
        try
        {
            // The install card closes with the window it stands for, and closing it cancels the download.
            RainAssetInstallWindow.CloseActive();
            try { if (App.Ownership != null) App.Ownership.OwnershipChanged -= OnOwnershipChanged; } catch (Exception ex) { Diag.Swallowed(ex); }
            // Panic or close mid-bake: the temp pack goes, the applied look and pack stay.
            try { _apply?.Abort(); } catch (Exception ex) { Diag.Swallowed(ex); }
            _apply = null;
            bool had = _host != null;
            try { _host?.Dispose(); } catch (Exception ex) { Diag.Swallowed(ex); }
            _host = null;
            if (had) App.Logger?.Information("RainWardrobe: closed");
        }
        finally { _disposing = false; }
    }

    private static void OnOwnershipChanged(object? sender, Prizes.OwnershipChangedEventArgs e)
    {
        RunOnUi(() =>
        {
            try
            {
                // rain-owned re-badges the shop layer in place; rain-locked keeps the ?locked= fallback in step.
                _host?.Post(new { type = "rain-owned", grants = AccountGrants() });
                _host?.Post(new { type = "rain-locked", locked = RainWardrobeCatalog.LockedKeys(App.Ownership?.Grants) });
            }
            catch (Exception ex) { App.Logger?.Debug("RainWardrobe: ownership post failed: {E}", ex.Message); }
        });
    }

    private static void OnPageMessage(JObject o)
    {
        try
        {
            var host = _host;
            if (host == null) return;
            Route(o, _apply, host.Post, BuildLiveInit, ShowCounterSoon);
        }
        catch (Exception ex) { App.Logger?.Warning("RainWardrobe: message handler threw: {E}", ex.Message); }
    }

    /// <summary>One page frame (testable): init on ready, Apply and bake frames to the controller, the
    /// counter door, and no per-change autosave (Apply is the only way a look reaches the tube).</summary>
    internal static void Route(JObject o, RainApplyController? apply, Action<object> post, Func<JObject> init, Action counterMissing)
    {
        var type = (string?)o["type"];
        if (apply != null && apply.Handle(o)) return;
        switch (type)
        {
            case "rain-ready":
                App.Logger?.Information("RainWardrobe: page ready");
                post(init());
                break;
            case "open-counter":
                var tab = (string?)o["tab"];
                var launch = CounterLauncher;
                if (launch != null) launch("wardrobe", string.IsNullOrWhiteSpace(tab) ? null : tab);
                else counterMissing();
                break;
            case "rain-look":
                // The pre-Apply autosave. Init always says applyMode:true, so a stray frame never saves.
                App.Logger?.Debug("RainWardrobe: rain-look ignored (Apply mode)");
                break;
            case "boot-error":
                // The page shows its own error card; the window stays so the user can read it.
                App.Logger?.Warning("RainWardrobe: page boot-error: {Msg}", (string?)o["msg"]);
                break;
        }
    }

    private static JObject BuildLiveInit()
    {
        var progress = new AppRainProgressSource();
        var settings = App.Settings?.Current;
        IReadOnlyDictionary<string, int>? prices = null;
        try { prices = CounterPrices?.Invoke(); } catch (Exception ex) { Diag.Swallowed(ex); }
        return RainApplyController.BuildInit(
            RainWardrobeCatalog.Clamp(settings?.RainLook, App.Ownership?.Grants),
            AccountGrants(), progress.Level, progress.UnlockedAchievements,
            LocalizationManager.Instance.CurrentLanguage,
            MotionFx.Level.ToString().ToLowerInvariant(),
            Safety.GentleVisuals.On, prices);
    }

    /// <summary>The grants the host's Apply clamp honours (PrizeGrants, so a dev override agrees with the page).</summary>
    private static IReadOnlyList<string> AccountGrants()
    {
        var set = new HashSet<string>(App.Ownership?.Grants ?? Array.Empty<string>(), StringComparer.Ordinal);
        foreach (var item in RainItemManifest.Shipped.Items)
            if (item.Grant != null && Prizes.PrizeGrants.IsGranted(item.Grant)) set.Add(item.Grant);
        return set.OrderBy(g => g, StringComparer.Ordinal).ToList();
    }

    private static void ShowCounterSoon()
        => App.Notifications?.Show(Loc.Get("rain_wardrobe_counter_soon"), NotificationType.Info, TimeSpan.FromSeconds(6));

    private static RainApplyController.Hooks ApplyHooks(ChaosWebViewHost host) => new()
    {
        Post = msg => host.Post(msg),
        Store = RainPackStore.CreateDefault,
        Access = RainItemAccess.ForApp,
        Clips = () => _clips ??= RainApplyController.ClipsFromEmotes(ReadEmbeddedEmotes()),
        Commit = CommitLook,
        Background = BakeQueue.Enqueue,
        Ui = RunOnUi,
        Log = line => App.Logger?.Information("RainWardrobe: {Line}", line),
        OnFailed = () => App.Notifications?.Show(Loc.Get("rain_wardrobe_apply_failed"), NotificationType.Info, TimeSpan.FromSeconds(6)),
    };

    /// <summary>Apply landed: the look and its pack become the tube's, saved, and the tube re-enters emote mode.</summary>
    private static void CommitLook(string look, string packHash)
    {
        var settings = App.Settings?.Current;
        if (settings == null) return;
        settings.RainLook = look;
        settings.RainPackHash = packHash;
        App.Settings!.Save();
        App.AvatarWindow?.RefreshRainLook();
        App.Logger?.Information("RainWardrobe: applied look {Hash}", packHash.Length > 0 ? packHash[..12] : "default");
    }

    private static string? ReadEmbeddedEmotes()
    {
        try
        {
            var sri = Application.GetResourceStream(new Uri("pack://application:,,,/Resources/rain_emotes/emotes.json", UriKind.Absolute));
            if (sri == null) return null;
            using var reader = new StreamReader(sri.Stream);
            return reader.ReadToEnd();
        }
        catch (Exception ex) { App.Logger?.Debug("RainWardrobe: emotes.json read failed: {E}", ex.Message); return null; }
    }

    /// <summary>Clamps (owned items only) and saves the look; false when nothing changed.</summary>
    internal static bool StoreLook(string? lookJson, IEnumerable<string>? grants)
    {
        var settings = App.Settings?.Current;
        if (settings == null) return false;
        var next = RainWardrobeCatalog.Clamp(lookJson, grants);
        if (string.Equals(next, RainLookCodec.Normalize(settings.RainLook), StringComparison.Ordinal)) return false;
        settings.RainLook = next;
        App.Settings!.Save();
        return true;
    }

    private static void RunOnUi(Action act)
    {
        var disp = Application.Current?.Dispatcher;
        if (disp == null) { try { act(); } catch (Exception ex) { Diag.Swallowed(ex); } return; }
        if (disp.HasShutdownStarted) return;
        disp.BeginInvoke(act, DispatcherPriority.Normal);
    }
}
