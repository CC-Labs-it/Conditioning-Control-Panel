// PORTED from ConditioningControlPanel/Services/Tc/TcHostService.Media.cs (main ed66bf9f44, 41c57f2de9, 7c9a76bca3):
// T&C's Scrolller pictures, "Use local files" from the app's own ticked pictures, and the Sparkles medal. Pictures
// are the shared pool on its own "tc" tenants; nothing is fetched before Core TcMediaRules.PicturesOn (online source,
// app consent, not offline, not switched off in this window). Files are served by this window's asset server, so the
// caps carry no mediaOrigin (the page then takes its own origin). Close and panic dispose the pool (CloseTc).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Fyp;
using ConditioningControlPanel.Services.Fyp.Online;
using ConditioningControlPanel.Services.GoonGame;
using ConditioningControlPanel.Services.Sparkles.Claims;
using ConditioningControlPanel.Services.Tc;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Games;

internal sealed partial class GameWindow
{
    /// <summary>Test seam: the "tc" pool (WPF GoonOnlineMedia.ForGame("tc"), stills and clips).</summary>
    internal static Func<Action<GoonOnlineMedia.Snapshot>, IGlassroadPictures> TcPoolFactory =
        cb => new GlassroadPool(GoonOnlineMedia.ForGame("tc", cb));

    /// <summary>Test seam: the app's consent question (null = the dialog every picture door uses).</summary>
    internal Func<Task<bool>>? TcAskConsent;

    /// <summary>Test seam: the library sources (WPF ReadLibrary: the Flash Images disk pool and the ticked videos).</summary>
    internal static Func<IEnumerable<TcOwnMediaRules.Source>> TcLibrarySources = () =>
    {
        var root = CorePaths.EffectiveAssets;
        return FlashOverlay.FlashFiles(CoreSettings.Current).Select(p => new TcOwnMediaRules.Source(p, false))
            .Concat(FypAssetManifest.Build(new FypMetaStore()).Where(e => e.Type == "video")
                .Select(e => new TcOwnMediaRules.Source(Path.Combine(root, e.Id.Replace('/', Path.DirectorySeparatorChar)), true)))
            .ToList();
    };

    private IGlassroadPictures? _tcPictures;
    private DispatcherTimer? _tcPicturesTimer;
    private IReadOnlyList<string> _tcStills = Array.Empty<string>(), _tcClips = Array.Empty<string>(), _tcNiches = Array.Empty<string>();
    private int _tcPictureOffset, _tcPictureVersion;
    private bool _tcPicturesOffInWindow, _tcOwnListInFlight;

    /// <summary>The last own-media-list read (tests await it).</summary>
    internal Task TcOwnMediaPending { get; private set; } = Task.CompletedTask;

    private bool TcPicturesOnNow
    {
        get
        {
            var s = CoreSettings.Current;
            return TcMediaRules.PicturesOn(s.MediaSource, s.HasRemoteMediaConsent, s.OfflineMode, _tcPicturesOffInWindow);
        }
    }

    /// <summary>WPF PostInit's media caps: device-origin pictures, the library, Sparkles when signed in.</summary>
    private static JObject TcMediaInit(JObject init)
    {
        var caps = (JObject)init["caps"]!;
        (caps["scrolller"] as JObject)?.Remove("mediaOrigin");   // rows come from this page's own origin
        (caps["ownMedia"] as JObject)?.Remove("mediaOrigin");
        return init;
    }

    private static bool TcSparklesOn => SparklesHead.Service != null && !string.IsNullOrEmpty(CoreAccount.UnifiedUserId);

    /// <summary>WPF OnPictureSetting: only a press (<c>ask</c>) can turn pictures on, and only through the app's
    /// consent, asked only when it can matter (41c57f2de9); off always works and only for this window.</summary>
    internal async Task SetTcPicturesAsync(TcPageFrame f)
    {
        var s = CoreSettings.Current;
        if (f.Value && f.Ask)
        {
            _tcPicturesOffInWindow = false;
            if (!s.HasRemoteMediaConsent && TcMediaRules.ConsentAskMatters(s.MediaSource, s.OfflineMode))
            {
                var (title, message) = AssetsTabView.RemoteMediaConsentText();
                bool yes = TcAskConsent != null ? await TcAskConsent()
                    : await MessageDialog.ConfirmAsync(this, title, message, okText: Loc.Get("label_yes"));
                if (!_tcOpen || _closing) return;   // panic during the question: nothing starts
                if (yes)
                {
                    s.RemoteMediaConsented = true;
                    try { CoreSettings.Save(); } catch (Exception ex) { Log.Debug("Tc: consent save: {E}", ex.Message); }
                }
            }
        }
        else if (!f.Value) _tcPicturesOffInWindow = true;
        bool on = TcPicturesOnNow;
        if (on) StartTcPictures(); else StopTcPictures();
        Post(TcMediaRules.SettingEcho(on, on || !f.Value ? null : TcMediaRules.RefusalReason(s.MediaSource, s.HasRemoteMediaConsent, s.OfflineMode)));
    }

    /// <summary>WPF OnPictureNiches: the subs come from Core TcMediaRules.ThemeSubs, never from the page.</summary>
    private void SetTcNiches(TcPageFrame f)
    {
        _tcNiches = f.Niches ?? Array.Empty<string>();
        if (TcPicturesOnNow) StartTcPictures();
    }

    private void StartTcPictures()
    {
        if (!_tcOpen || _closing || _server == null || !TcPicturesOnNow) return;
        try
        {
            if (_tcPictures == null)
            {
                int version = ++_tcPictureVersion;
                _tcPictures = TcPoolFactory(snap => OnTcSnapshot(version, snap));
            }
            _tcPictures.Start(TcMediaRules.SubsFor(_tcNiches).ToList());
            if (_tcPicturesTimer == null)
            {
                _tcPicturesTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(90) };
                _tcPicturesTimer.Tick += (_, _) => { _tcPictureOffset += TcMediaRules.MaxRows / 3; PostTcFeed(); };
                _tcPicturesTimer.Start();
            }
        }
        catch (Exception ex) { Log.Warning("Tc: pictures start failed: {E}", ex.Message); }
    }

    /// <summary>Stops fetching and releases every temp file; queued snapshots are dropped by the version.</summary>
    private void StopTcPictures()
    {
        ++_tcPictureVersion;
        try { _tcPictures?.Dispose(); } catch (Exception ex) { Diag.Swallowed(ex); }
        _tcPictures = null;
        _tcPicturesTimer?.Stop();
        _tcPicturesTimer = null;
        _tcStills = _tcClips = Array.Empty<string>();
        PostTcFeed();
    }

    internal bool TcPicturesRunning => _tcPictures != null;

    private void OnTcSnapshot(int version, GoonOnlineMedia.Snapshot snap)
    {
        var stills = snap.Images.Select(i => i.Path).ToArray();
        var clips = snap.Videos.Select(i => i.Path).ToArray();
        Dispatcher.UIThread.Post(() =>
        {
            if (!_tcOpen || _closing || version != _tcPictureVersion || _server is not { } server) return;
            var root = CorePaths.EffectiveAssets;
            _tcStills = stills.Select(p => server.AssetUrl(Path.GetRelativePath(root, p))).ToArray();
            _tcClips = clips.Select(p => server.AssetUrl(Path.GetRelativePath(root, p))).ToArray();
            PostTcFeed();
        });
    }

    private void PostTcFeed()
    {
        if (_tcOpen && !_closing) Post(TcMediaRules.Feed(_tcStills, _tcClips, _tcPictureOffset));
    }

    /// <summary>WPF DisposePictures, from CloseTc (panic, X, exit): fetching stops on the same call.</summary>
    private void DisposeTcPictures()
    {
        ++_tcPictureVersion;
        try { _tcPictures?.Dispose(); } catch (Exception ex) { Diag.Swallowed(ex); }
        _tcPictures = null;
        _tcPicturesTimer?.Stop();
        _tcPicturesTimer = null;
        _tcStills = _tcClips = _tcNiches = Array.Empty<string>();
        _tcPictureOffset = 0;
        _tcPicturesOffInWindow = false;
    }

    /// <summary>WPF OnOwnMediaList (7c9a76bca3): the ticked library, read off the UI thread, one read at a time,
    /// posted only to this window while it is open.</summary>
    private void OnTcOwnMediaList()
    {
        if (_tcOwnListInFlight || _server is not { } server) return;
        _tcOwnListInFlight = true;
        var root = CorePaths.EffectiveAssets;
        TcOwnMediaPending = Task.Run(() => TcOwnMediaRules.Frame(TcOwnMediaRules.Rows(TcLibrarySources(), root, TcWebpAnimates,
                TcStat, new Random(), server.AssetUrl)))
            .ContinueWith(t => Dispatcher.UIThread.InvokeAsync(() =>
            {
                _tcOwnListInFlight = false;
                if (t.Status != TaskStatus.RanToCompletion) { Log.Debug("Tc: own library failed ({E})", t.Exception?.GetBaseException().GetType().Name); return; }
                if (!_tcOpen || _closing) return;
                Log.Information("Tc: own library answered with {N} files", ((JArray)t.Result["rows"]!).Count);
                Post(t.Result);
            }).GetTask(), TaskScheduler.Default).Unwrap();
    }

    private static bool TcWebpAnimates(string path)
    {
        try
        {
            var head = new byte[30];
            using var fs = File.OpenRead(path);
            return ScrolllerDiskCache.IsAnimatedWebp(head, fs.Read(head, 0, head.Length));
        }
        catch { return false; }
    }

    private static TcOwnMediaRules.Stat? TcStat(string path)
    {
        try
        {
            var fi = new FileInfo(path);
            return fi.Exists ? new TcOwnMediaRules.Stat(fi.Length, new DateTimeOffset(fi.LastWriteTimeUtc).ToUnixTimeMilliseconds()) : null;
        }
        catch { return null; }
    }

    /// <summary>WPF OnRunEnded: always the same claim (finished fee, ending id and minutes); the server prices it.</summary>
    private void OnTcRunEnded(TcPageFrame f)
    {
        if (string.IsNullOrEmpty(f.Ending)) return;
        DeliverSparkles(SparklesGameEnd.Claim(SparklesHead.Service, TcId, "solo", SparklesResult.ForMedal(TcRules.EndingMedal),
            TimeSpan.FromMinutes(f.Minutes), TcMediaRules.ClaimDetail(f.Ending, f.Minutes)));
    }
}
