// PORTED from WPF GlassroadHostService.Pictures.cs: the race's stills-only Scrolller bridge.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using ConditioningControlPanel.Services.Fyp.Online;
using ConditioningControlPanel.Services.Glassroad;
using ConditioningControlPanel.Services.GoonGame;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Games;

internal sealed partial class GameWindow
{
    internal interface IGlassroadPictures : IDisposable
    {
        void Start(IReadOnlyList<string> channels);
    }

    private sealed class GlassroadPool : IGlassroadPictures
    {
        private readonly GoonOnlineMedia _pool;
        public GlassroadPool(Action<GoonOnlineMedia.Snapshot> snapshot)
        {
            // The Core pool owns these copies until Off or Dispose; the server serves this copy prefix only.
            GoonOnlineMedia.Materialize = DtrhRemoteMedia.AssetsTempCopier.Materialize;
            GoonOnlineMedia.ReleaseTempFile = DtrhRemoteMedia.AssetsTempCopier.Release;
            _pool = GoonOnlineMedia.ForGameStills("race3d", snapshot);
        }
        public void Start(IReadOnlyList<string> channels) => _pool.Start(channels);
        public void Dispose() => _pool.Dispose();
    }

    internal static Func<Action<GoonOnlineMedia.Snapshot>, IGlassroadPictures> GlassroadPoolFactory = callback => new GlassroadPool(callback);
    internal Func<Task<bool>>? GlassroadAskConsent;
    private IGlassroadPictures? _grPictures;
    private DispatcherTimer? _grPicturesTimer;
    private IReadOnlyList<string> _grPictureUrls = Array.Empty<string>();
    private List<string> _grNiches = new();
    private IReadOnlyList<string> _grChannels = Array.Empty<string>();
    private int _grPictureOffset, _grPictureVersion, _grSettingVersion;
    private bool _grWritingSetting;

    private static bool GlassroadPicturesOn()
    {
        var s = CoreSettings.Current;
        return GlassroadPictures.On(s.GlassroadPictures, s.MediaSource, s.HasRemoteMediaConsent, s.OfflineMode,
            GlassroadHostRules.RemotePicturesOffered);
    }

    internal async Task SetGlassroadPicturesAsync(JObject o)
    {
        if ((string?)o["key"] != GlassroadPictures.SettingKey || !_grOpen || _closing) return;
        var version = ++_grSettingVersion;
        var s = CoreSettings.Current;
        bool want = o["value"]?.Type == JTokenType.Boolean && (bool)o["value"]!;
        bool asked = o["ask"]?.Type == JTokenType.Boolean && (bool)o["ask"]!;
        if (want && !GlassroadHostRules.RemotePicturesOffered)
        {
            // B2: this host offers no Scrolller pictures. Nothing is asked, nothing is written; the page hears no
            // and keeps My files / Off.
            Post(new { type = "setting", key = GlassroadPictures.SettingKey, value = false });
            return;
        }
        try
        {
            bool consent = false, newConsent = false;
            if (want && asked)
            {
                consent = s.HasRemoteMediaConsent;
                if (!consent)
                {
                    var (title, message) = AssetsTabView.RemoteMediaConsentText();
                    consent = GlassroadAskConsent != null ? await GlassroadAskConsent()
                        : await MessageDialog.ConfirmAsync(this, title, message, okText: Loc.Get("label_yes"));
                    if (!_grOpen || _closing || version != _grSettingVersion) return;
                    newConsent = consent;
                }
            }
            // The page treats its first echo as the answer, so publish only after both writes.
            _grWritingSetting = true;
            try
            {
                if (newConsent) s.RemoteMediaConsented = true;
                if (want && asked && consent) s.GlassroadPictures = true;
                else if (!want) s.GlassroadPictures = false;
                CoreSettings.Save();
            }
            finally { _grWritingSetting = false; }
            bool on = GlassroadPicturesOn();
            if (on) StartGlassroadPictures(); else StopGlassroadPictures();
            Post(new { type = "setting", key = GlassroadPictures.SettingKey, value = on });
        }
        catch (Exception ex) { Log.Warning("Glassroad: picture setting failed: {E}", ex.Message); }
    }

    private void SetGlassroadNiches(JObject o)
    {
        _grNiches = (o["niches"] as JArray)?.Where(t => t.Type == JTokenType.String)
            .Select(t => (string)t!).Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToList() ?? new List<string>();
        if (GlassroadPicturesOn()) StartGlassroadPictures();
    }

    private void StartGlassroadPictures()
    {
        if (!_grOpen || _closing || _server == null || !GlassroadPicturesOn()) return;
        try
        {
            var s = CoreSettings.Current;
            var subs = FypOnlineCoordinator.ResolveChannels(_grNiches, null);
            if (subs.Count == 0) subs = FypOnlineCoordinator.ResolveChannels(s.FypOnlineNiches, s.FypOnlineCustomSubs);
            if (_grPictures != null && !subs.SequenceEqual(_grChannels, StringComparer.OrdinalIgnoreCase))
            {
                ++_grPictureVersion;
                _grPictures.Dispose();
                _grPictures = null;
                _grPictureUrls = Array.Empty<string>();
                PostGlassroadFeed();
            }
            if (_grPictures == null)
            {
                int version = ++_grPictureVersion;
                _grPictures = GlassroadPoolFactory(snap => OnGlassroadSnapshot(version, snap));
                _grChannels = subs.ToArray();
            }
            _grPictures.Start(subs);
            if (_grPicturesTimer == null)
            {
                _grPicturesTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(90) };
                _grPicturesTimer.Tick += (_, _) => { _grPictureOffset += GlassroadPictures.Step; PostGlassroadFeed(); };
                _grPicturesTimer.Start();
            }
        }
        catch (Exception ex) { Log.Warning("Glassroad: pictures start failed: {E}", ex.Message); }
    }

    private void StopGlassroadPictures()
    {
        // Dispose also invalidates any snapshots already queued on the UI thread.
        ++_grPictureVersion;
        try { _grPictures?.Dispose(); } catch (Exception ex) { Diag.Swallowed(ex); }
        _grPictures = null;
        _grChannels = Array.Empty<string>();
        _grPicturesTimer?.Stop();
        _grPicturesTimer = null;
        _grPictureUrls = Array.Empty<string>();
        PostGlassroadFeed();
    }

    private void OnGlassroadSnapshot(int version, GoonOnlineMedia.Snapshot snap)
    {
        var paths = snap.Images.Select(i => i.Path).ToArray();
        Dispatcher.UIThread.Post(() =>
        {
            if (!_grOpen || _closing || version != _grPictureVersion || !GlassroadPicturesOn() || _server == null) return;
            var root = CorePaths.EffectiveAssets;
            _grPictureUrls = paths.Select(path => _server.AssetUrl(Path.GetRelativePath(root, path))).ToArray();
            PostGlassroadFeed();
        });
    }

    private void PostGlassroadFeed()
    {
        if (!_grOpen || _closing) return;
        Post(GlassroadPictures.Feed(_grPictureUrls, _grPictureOffset,
            FypOnlineCoordinator.Catalog.Select(n => (n.Id, n.Label))));
    }

    private void OnGlassroadSettingsChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_grWritingSetting) return;
        if (e.PropertyName is not ("OfflineMode" or "MediaSource" or "RemoteMediaConsented" or "FypOnlineConsented" or "GlassroadPictures")) return;
        void Apply()
        {
            if (!_grOpen || _closing) return;
            if (GlassroadPicturesOn()) StartGlassroadPictures(); else StopGlassroadPictures();
            Post(new { type = "setting", key = GlassroadPictures.SettingKey, value = GlassroadPicturesOn() });
        }
        if (Dispatcher.UIThread.CheckAccess()) Apply(); else Dispatcher.UIThread.Post(Apply);
    }

    private void DisposeGlassroadPictures()
    {
        ++_grSettingVersion;
        StopGlassroadPictures();
        _grNiches.Clear();
        _grPictureOffset = 0;
    }
}
