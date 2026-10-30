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
    private static string? _tryOn;

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
    /// locked items and the localised lock line. Never the review or QA studio. The boot cover needs its
    /// facts before init (init only answers rain-ready, ~30 s in): the companion's name, the cover's
    /// localised lines, Calm, and the big files' sizes for a true progress bar.</summary>
    internal static string PageUrl(string? storedLook, IEnumerable<string>? grants, string lockNote,
        string? companion = null, IReadOnlyDictionary<string, string>? bootStrings = null, bool calm = false,
        IReadOnlyDictionary<string, long>? sizes = null)
    {
        var look = RainWardrobeCatalog.Clamp(storedLook, grants);
        var q = "host=ccp";
        if (look.Length > 0) q += "&look=" + Uri.EscapeDataString(look);
        var locked = RainWardrobeCatalog.LockedKeys(grants);
        if (locked.Count > 0) q += "&locked=" + Uri.EscapeDataString(string.Join(",", locked));
        if (!string.IsNullOrWhiteSpace(lockNote)) q += "&lockNote=" + Uri.EscapeDataString(lockNote);
        if (!string.IsNullOrWhiteSpace(companion)) q += "&companion=" + Uri.EscapeDataString(companion);
        if (bootStrings is { Count: > 0 }) q += "&strings=" + Uri.EscapeDataString(JObject.FromObject(bootStrings).ToString(Newtonsoft.Json.Formatting.None));
        if (calm) q += "&calm=1";
        if (sizes is { Count: > 0 })
            q += "&sizes=" + Uri.EscapeDataString(string.Join(",", sizes.Where(kv => kv.Value > 0).Select(kv => $"{kv.Key}:{kv.Value}")));
        return $"https://{PageHost}/{PagePath}?{q}";
    }

    /// <summary>The counter's Wear door: open (or focus) with one items.json key (<c>slot:pageId</c>) tried
    /// on as a draft (<c>init.tryOn</c>, or <c>rain-tryon</c> to a live window). An unknown key opens plain.
    /// False when the window is not up (assets missing, open failed).</summary>
    public static bool Open(string? tryOn)
    {
        var key = TryOnKey(tryOn, RainItemManifest.Shipped);
        if (_host != null)
        {
            if (key != null) _host.Post(new { type = "rain-tryon", key });
            _host.FocusWeb();
            return true;
        }
        _tryOn = key;
        Open();
        if (_host == null) _tryOn = null;
        return _host != null;
    }

    /// <summary>The key when items.json knows it, else null (only shipped items are ever tried on).</summary>
    internal static string? TryOnKey(string? key, RainItemManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        int colon = key.IndexOf(':');
        return colon > 0 && manifest.Find(key[..colon], key[(colon + 1)..]) is { } item ? item.Id : null;
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
                StartUrl = PageUrl(settings?.RainLook, grants, Loc.Get("rain_wardrobe_locked"),
                    CompanionName.ForActiveMod(), BootStrings(Loc.Get), BootCalm(), AssetSizes(dir)),
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
                    if (!ClosesOn(kind))
                    {
                        App.Logger?.Information("RainWardrobe: page busy ({Kind}), kept open", kind);
                        return;
                    }
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

    /// <summary>Which process failures close the window. RenderProcessUnresponsive does not: the renderer is alive
    /// and busy. The page parses ~300 MB of Rain (avatar.glb + pelvis-data.js) for about 30 s on boot, and any key
    /// or click sent to the window meanwhile trips Chromium's hang monitor (desk 2026-10-30: stray Spaces after a
    /// counter Wear closed the dressing room 26 s in). The title-bar X and panic still close it.</summary>
    internal static bool ClosesOn(CoreWebView2ProcessFailedKind kind) => kind != CoreWebView2ProcessFailedKind.RenderProcessUnresponsive;

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
            _tryOn = null;
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
            Route(o, _apply, host.Post, BuildLiveInit, ShowCounterSoon, RainLookSync.LookOwingABake);
        }
        catch (Exception ex) { App.Logger?.Warning("RainWardrobe: message handler threw: {E}", ex.Message); }
    }

    /// <summary>One page frame (testable): init on ready, Apply and bake frames to the controller, the
    /// counter door, and no per-change autosave (Apply is the only way a look reaches the tube).</summary>
    internal static void Route(JObject o, RainApplyController? apply, Action<object> post, Func<JObject> init, Action counterMissing,
        Func<string?>? lookOwingABake = null)
    {
        var type = (string?)o["type"];
        if (apply != null && apply.Handle(o)) return;
        switch (type)
        {
            case "rain-ready":
                App.Logger?.Information("RainWardrobe: page ready");
                post(init());
                // Rain look sync: a look adopted from another device (or restored) has no pack on this PC.
                // The page now shows it, so a host-side Apply bakes it (the same clamp, bake and commit).
                if (apply != null && !apply.IsBaking && lookOwingABake?.Invoke() is { Length: > 0 } owed)
                {
                    App.Logger?.Information("RainWardrobe: re-baking the synced look");
                    apply.Handle(new JObject { ["type"] = "rain-apply", ["look"] = JObject.Parse(owed) });
                }
                break;
            case "open-counter":
                var tab = (string?)o["tab"];
                var launch = CounterLauncher;
                if (launch != null) launch("rain-wardrobe", string.IsNullOrWhiteSpace(tab) ? null : tab);
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
        var init = RainApplyController.BuildInit(
            RainWardrobeCatalog.Clamp(settings?.RainLook, App.Ownership?.Grants),
            AccountGrants(), progress.Level, progress.UnlockedAchievements,
            LocalizationManager.Instance.CurrentLanguage,
            MotionFx.Level.ToString().ToLowerInvariant(),
            Safety.GentleVisuals.On, prices, PageStrings(Loc.Get));
        // What the page calls her: the active mod's name (Bambi, Bimbo, Emi), never the model's "Rain".
        init["companion"] = CompanionName.ForActiveMod();
        // The counter's Wear door: one piece on as a draft, once (a later ready never re-applies it).
        if (_tryOn != null) { init["tryOn"] = _tryOn; _tryOn = null; }
        return init;
    }

    /// <summary>Loc keys behind the page's bake lines (<c>wardrobe-shop.js STR</c> key -> loc key).</summary>
    internal static readonly IReadOnlyDictionary<string, string> PageStringKeys = new Dictionary<string, string>
    {
        ["applying"] = "rain_wardrobe_applying",
        ["baking"] = "rain_wardrobe_baking",
        ["bakeHint"] = "rain_wardrobe_bake_hint",
    };

    /// <summary>Loc keys behind the boot cover's lines (<c>wardrobe-loading.js LOAD_STR</c> key -> loc key),
    /// sent in the page URL; Loc fills <c>{companion}</c> here, the page fills the byte counts.</summary>
    internal static readonly IReadOnlyDictionary<string, string> BootStringKeys = new Dictionary<string, string>
    {
        ["loadTitle"] = "rain_wardrobe_load_title",
        ["loadSize"] = "rain_wardrobe_load_size",
        ["loadAlmost"] = "rain_wardrobe_load_almost",
    };

    /// <summary>The boot cover's lines in the user's language; a missing key stays English.</summary>
    internal static IReadOnlyDictionary<string, string> BootStrings(Func<string, string> loc) => Localised(BootStringKeys, loc);

    /// <summary>Calm for the boot cover (Gentle Visuals, or motion below Full): its fades only lengthen.</summary>
    private static bool BootCalm()
    {
        try { return Safety.GentleVisuals.On || MotionFx.Level != Models.MotionLevel.Full; }
        catch (Exception ex) { Diag.Swallowed(ex); return false; }
    }

    /// <summary>The big files' sizes on disk (the page's progress totals); a missing file is left out.</summary>
    internal static IReadOnlyDictionary<string, long> AssetSizes(string? dir)
    {
        var map = new Dictionary<string, long>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(dir)) return map;
        foreach (var f in RainWardrobeAssets.Files)
        {
            try { var info = new FileInfo(Path.Combine(dir, f)); if (info.Exists) map[f] = info.Length; }
            catch (Exception ex) { Diag.Swallowed(ex); }
        }
        return map;
    }

    /// <summary>The page's bake lines in the user's language; a missing key (Loc echoes it) stays English.</summary>
    internal static IReadOnlyDictionary<string, string> PageStrings(Func<string, string> loc) => Localised(PageStringKeys, loc);

    private static IReadOnlyDictionary<string, string> Localised(IReadOnlyDictionary<string, string> keys, Func<string, string> loc)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (page, key) in keys)
        {
            var text = loc(key);
            if (!string.IsNullOrWhiteSpace(text) && !string.Equals(text, key, StringComparison.Ordinal)) map[page] = text;
        }
        return map;
    }

    /// <summary>The bake as the window shows it outside the page: the title carries "n of 17" and the
    /// taskbar button fills, so a user who switched away still sees it move. Null puts both back.</summary>
    internal static string TitleFor(string title, RainBakeProgress? progress, string bakingLine)
        => progress is { ClipsTotal: > 0 } p ? $"{title} - {p.Fill(bakingLine)}" : title;

    private static void ShowProgress(ChaosWebViewHost host, RainBakeProgress? progress)
    {
        try
        {
            var window = host.Window;
            if (window == null) return;
            window.Title = TitleFor(Loc.Get("rain_wardrobe_title"), progress, Loc.Get("rain_wardrobe_baking"));
            if (progress is { ClipsTotal: > 0 } p)
            {
                window.TaskbarItemInfo ??= new System.Windows.Shell.TaskbarItemInfo();
                window.TaskbarItemInfo.ProgressState = System.Windows.Shell.TaskbarItemProgressState.Normal;
                window.TaskbarItemInfo.ProgressValue = p.Fraction;
            }
            else if (window.TaskbarItemInfo != null)
                window.TaskbarItemInfo.ProgressState = System.Windows.Shell.TaskbarItemProgressState.None;
        }
        catch (Exception ex) { Diag.Swallowed(ex); }
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
        Progress = p => ShowProgress(host, p),
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
        // The look follows the account (/v2/equip family rain); the pack stays on this PC.
        RainLookSync.NoteLocalChange();
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
