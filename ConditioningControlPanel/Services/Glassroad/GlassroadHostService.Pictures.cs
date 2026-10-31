using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using ConditioningControlPanel.Services.Fyp.Online;
using ConditioningControlPanel.Services.GoonGame;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Glassroad;

// ============================ Scrolller pictures (2026-10-31) ============================
//
// The race's world screens and flashes show Scrolller stills on the desktop too, on the phone's
// wire (glassroad CONTRACT.md section 7, device origin): caps.scrolller, set-setting, setting,
// scrolller-niches, scrolller-feed. The fetch is the shared pool (GoonOnlineMedia, stills only),
// so the daily share, the 24 h rule, the off switch and temp-file ownership are the same as
// chess. Every file is a plain https://ccp.assets/.temp/... url the page draws into a canvas.
//
// Nothing is fetched before the gate (GlassroadRules.PicturesOn), and a press with no app consent
// asks the app's own consent question first. Every exit runs DisposePictures through Close.
internal static partial class GlassroadHostService
{
    private const string PicturesTenant = "race3d";
    private static readonly TimeSpan PicturesTurn = TimeSpan.FromSeconds(90);

    private static GoonOnlineMedia? _pictures;
    private static DispatcherTimer? _picturesTimer;
    private static IReadOnlyList<string> _pictureUrls = Array.Empty<string>();
    private static List<string> _pictureNiches = new();
    private static int _pictureOffset;

    private static bool PicturesOnNow
    {
        get
        {
            var s = App.Settings?.Current;
            return s != null && GlassroadRules.PicturesOn(s.GlassroadPictures, s.MediaSource, s.HasRemoteMediaConsent, s.OfflineMode);
        }
    }

    /// <summary>page -&gt; host <c>set-setting { key, value, ask }</c>: the race's own switch. <c>ask</c> is only ever a
    /// player's press; anything else can turn pictures off and never on.</summary>
    private static void OnPictureSetting(JObject o)
    {
        if ((string?)o["key"] != GlassroadRules.PicturesSettingKey) return;
        var s = App.Settings?.Current;
        if (s == null) return;
        bool want = o["value"]?.Type == JTokenType.Boolean && (bool)o["value"]!;
        bool asked = o["ask"]?.Type == JTokenType.Boolean && (bool)o["ask"]!;
        if (want && asked)
        {
            if (s.HasRemoteMediaConsent || AskPictureConsent(s)) s.GlassroadPictures = true;
        }
        else if (!want)
        {
            s.GlassroadPictures = false;
        }
        try { App.Settings?.Save(); } catch (Exception ex) { App.Logger?.Debug("Glassroad: pictures save: {E}", ex.Message); }
        bool on = PicturesOnNow;
        if (on) StartPictures();
        else StopPictures();
        try { _host?.Post(new { type = "setting", key = GlassroadRules.PicturesSettingKey, value = on }); }
        catch (Exception ex) { Diag.Swallowed(ex); }
    }

    /// <summary>The app's own consent question, word for word (LauncherMediaDialog), over the race window.</summary>
    private static bool AskPictureConsent(Models.AppSettings s)
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
        if (answer != MessageBoxResult.Yes) return false;
        s.RemoteMediaConsented = true;
        return true;
    }

    private static string LocOr(string key, string fallback)
    {
        var v = Localization.Loc.Get(key);
        return string.IsNullOrEmpty(v) || v == key ? fallback : v;
    }

    /// <summary>page -&gt; host <c>scrolller-niches { track, niches }</c>: this course's picks. The ids are looked up in
    /// the catalog, so the page can never steer the fetch anywhere else.</summary>
    private static void OnPictureNiches(JObject o)
    {
        _pictureNiches = (o["niches"] as JArray)?.Where(t => t.Type == JTokenType.String)
            .Select(t => (string)t!).Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToList() ?? new List<string>();
        if (PicturesOnNow) StartPictures();
    }

    private static void StartPictures()
    {
        try
        {
            if (_host == null) return;
            var s = App.Settings?.Current;
            var subs = FypOnlineCoordinator.ResolveChannels(_pictureNiches, null);
            if (subs.Count == 0) subs = FypOnlineCoordinator.ResolveChannels(s?.FypOnlineNiches, s?.FypOnlineCustomSubs);
            _pictures ??= GoonOnlineMedia.ForGameStills(PicturesTenant, OnPictureSnapshot);
            _pictures.Start(subs);
            if (_picturesTimer == null)
            {
                _picturesTimer = new DispatcherTimer { Interval = PicturesTurn };
                _picturesTimer.Tick += (_, _) => { _pictureOffset += GlassroadRules.PicturesStep; PostPictureFeed(); };
                _picturesTimer.Start();
            }
        }
        catch (Exception ex) { App.Logger?.Warning("Glassroad: pictures start failed: {E}", ex.Message); }
    }

    private static void StopPictures()
    {
        try { _pictures?.Off(); } catch (Exception ex) { Diag.Swallowed(ex); }
        _pictureUrls = Array.Empty<string>();
        PostPictureFeed();
    }

    /// <summary>Worker thread -&gt; UI thread: the pool's whole current list.</summary>
    private static void OnPictureSnapshot(GoonOnlineMedia.Snapshot snap)
    {
        var urls = snap.Images.Select(i => i.Url).ToList();
        RunOnUi(() => { _pictureUrls = urls; PostPictureFeed(); });
    }

    private static void PostPictureFeed()
    {
        try
        {
            var frame = GlassroadRules.PicturesFeed(_pictureUrls, _pictureOffset,
                FypOnlineCoordinator.Catalog.Select(n => (n.Id, n.Label)));
            _core?.PostWebMessageAsJson(frame.ToString(Newtonsoft.Json.Formatting.None));
        }
        catch (Exception ex) { App.Logger?.Debug("Glassroad: pictures post failed: {E}", ex.Message); }
    }

    /// <summary>Close: stop fetching and hand back every temp file this window owned.</summary>
    private static void DisposePictures()
    {
        try { _picturesTimer?.Stop(); } catch (Exception ex) { Diag.Swallowed(ex); }
        _picturesTimer = null;
        try { _pictures?.Dispose(); } catch (Exception ex) { Diag.Swallowed(ex); }
        _pictures = null;
        _pictureUrls = Array.Empty<string>();
        _pictureNiches = new List<string>();
        _pictureOffset = 0;
    }
}
