using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using ConditioningControlPanel.Services.GoonGame;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Tc;

// ============================ Scrolller pictures + the Sparkles medal ============================
//
// Pictures follow the Glassroad host (device origin): caps.scrolller, set-setting, setting, scrolller-niches,
// scrolller-feed. The fetch is the shared pool (GoonOnlineMedia, own "tc" tenants), so the daily share, the
// 24 h rule, the off switch and temp-file ownership are the ones every game uses; each file is a plain
// https://ccp.assets/.temp/... url. Nothing is fetched before TcMediaRules.PicturesOn (app online source,
// app consent, not offline). This host does not advertise sessionOptIn or playWarmup, so scrolller-release,
// scrolller-play and scrolller-view are only logged. Every exit runs DisposePictures through Close.
internal static partial class TcHostService
{
    private const string PicturesTenant = "tc";
    private static readonly TimeSpan PicturesTurn = TimeSpan.FromSeconds(90);

    private static GoonOnlineMedia? _pictures;
    private static DispatcherTimer? _picturesTimer;
    private static IReadOnlyList<string> _pictureStills = Array.Empty<string>();
    private static IReadOnlyList<string> _pictureClips = Array.Empty<string>();
    private static IReadOnlyList<string> _pictureNiches = Array.Empty<string>();
    private static int _pictureOffset;
    private static bool _picturesOffInWindow;

    private static bool PicturesOnNow
    {
        get
        {
            var s = App.Settings?.Current;
            return s != null && TcMediaRules.PicturesOn(s.MediaSource, s.HasRemoteMediaConsent, s.OfflineMode, _picturesOffInWindow);
        }
    }

    /// <summary>page -&gt; host <c>set-setting media.remoteConsent</c>. Only a player's press (<c>ask</c>) can turn
    /// pictures on, and then only through the app's own consent; off always works and only for this window.</summary>
    private static void OnPictureSetting(TcPageFrame f)
    {
        var s = App.Settings?.Current;
        if (s == null || _host == null) return;
        if (f.Value && f.Ask)
        {
            _picturesOffInWindow = false;
            // The question is asked only when its answer can matter (never on a local source or offline).
            if (!s.HasRemoteMediaConsent && TcMediaRules.ConsentAskMatters(s.MediaSource, s.OfflineMode)
                && AskPictureConsent())
            {
                s.RemoteMediaConsented = true;
                try { App.Settings?.Save(); } catch (Exception ex) { App.Logger?.Debug("Tc: consent save: {E}", ex.Message); }
            }
        }
        else if (!f.Value)
        {
            _picturesOffInWindow = true;
        }
        bool on = PicturesOnNow;
        if (on) StartPictures();
        else StopPictures();
        var reason = on || !f.Value ? null : TcMediaRules.RefusalReason(s.MediaSource, s.HasRemoteMediaConsent, s.OfflineMode);
        try { _host?.Post(TcMediaRules.SettingEcho(on, reason)); } catch (Exception ex) { Diag.Swallowed(ex); }
    }

    /// <summary>The app's own consent question, the same words as every other picture door.</summary>
    private static bool AskPictureConsent()
    {
        var message = LocOr("msg_remote_media_consent",
            "Pull media from Reddit?\n\n" +
            "The app will stream images and clips from the subreddits you pick, straight from your own machine. " +
            "Nothing is saved to your disk, nothing is uploaded, and none of it goes through our servers.\n\n" +
            "It is adult content and it is not curated by us - you choose the niches and subreddits, and only those are ever fetched.\n\n" +
            "Turn it on?");
        var title = LocOr("title_remote_media_consent", "Use Reddit media?");
        var owner = _host?.Window;
        var answer = owner != null
            ? MessageBox.Show(owner, message, title, MessageBoxButton.YesNo, MessageBoxImage.Question)
            : MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question);
        return answer == MessageBoxResult.Yes;
    }

    private static string LocOr(string key, string fallback)
    {
        var v = Localization.Loc.Get(key);
        return string.IsNullOrEmpty(v) || v == key ? fallback : v;
    }

    /// <summary>page -&gt; host <c>scrolller-niches</c>: theme ids, already filtered by <see cref="TcFrameGate"/>;
    /// the subs come from <see cref="TcMediaRules.ThemeSubs"/>, never from the page.</summary>
    private static void OnPictureNiches(TcPageFrame f)
    {
        _pictureNiches = f.Niches ?? Array.Empty<string>();
        if (PicturesOnNow) StartPictures();
    }

    private static void StartPictures()
    {
        try
        {
            if (_host == null) return;
            _pictures ??= GoonOnlineMedia.ForGame(PicturesTenant, OnPictureSnapshot);
            _pictures.Start(TcMediaRules.SubsFor(_pictureNiches).ToList());
            if (_picturesTimer == null)
            {
                _picturesTimer = new DispatcherTimer { Interval = PicturesTurn };
                _picturesTimer.Tick += (_, _) => { _pictureOffset += TcMediaRules.MaxRows / 3; PostPictureFeed(); };
                _picturesTimer.Start();
            }
        }
        catch (Exception ex) { App.Logger?.Warning("Tc: pictures start failed: {E}", ex.Message); }
    }

    private static void StopPictures()
    {
        try { _pictures?.Off(); } catch (Exception ex) { Diag.Swallowed(ex); }
        _pictureStills = Array.Empty<string>();
        _pictureClips = Array.Empty<string>();
        PostPictureFeed();
    }

    /// <summary>Worker thread -&gt; UI thread: the pool's whole current list.</summary>
    private static void OnPictureSnapshot(GoonOnlineMedia.Snapshot snap)
    {
        var stills = snap.Images.Select(i => i.Url).ToList();
        var clips = snap.Videos.Select(i => i.Url).ToList();
        RunOnUi(() => { _pictureStills = stills; _pictureClips = clips; PostPictureFeed(); });
    }

    private static void PostPictureFeed()
    {
        try { _host?.Post(TcMediaRules.Feed(_pictureStills, _pictureClips, _pictureOffset)); }
        catch (Exception ex) { App.Logger?.Debug("Tc: pictures post failed: {E}", ex.Message); }
    }

    /// <summary>Close: stop fetching and hand back every temp file this window owned.</summary>
    private static void DisposePictures()
    {
        try { _picturesTimer?.Stop(); } catch (Exception ex) { Diag.Swallowed(ex); }
        _picturesTimer = null;
        try { _pictures?.Dispose(); } catch (Exception ex) { Diag.Swallowed(ex); }
        _pictures = null;
        _pictureStills = Array.Empty<string>();
        _pictureClips = Array.Empty<string>();
        _pictureNiches = Array.Empty<string>();
        _pictureOffset = 0;
        _picturesOffInWindow = false;
    }

    /// <summary>page -&gt; host <c>fuzz-ended</c>: always the same claim (the finished fee, the ending id and minutes
    /// in detail). The server prices it, including the first-ever gold and the one-a-day cap; the receipt goes
    /// back to the page unchanged as <c>sparkles-receipt</c>.</summary>
    private static void OnRunEnded(TcPageFrame f)
    {
        if (string.IsNullOrEmpty(f.Ending)) return;
        var slipHost = _host;
        _ = Sparkles.SparklesSlip.Deliver(Sparkles.SparklesGameEnd.Claim(GameId, "solo",
                Sparkles.SparklesResult.ForMedal(TcRules.EndingMedal), TimeSpan.FromMinutes(f.Minutes),
                TcMediaRules.ClaimDetail(f.Ending, f.Minutes)),
            json => slipHost?.Post(new JRaw(json)));
    }
}
