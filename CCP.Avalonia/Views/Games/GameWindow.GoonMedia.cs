using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Avalonia.Threading;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.GoonGame;
using ConditioningControlPanel.Services.Chaos;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Avalonia.Views.Games;

internal sealed partial class GameWindow
{
    private GoonOnlineMedia? _goonMedia, _goonPeerMedia;
    private readonly Dictionary<string, GoonOnlineMedia> _goonNoise = new(StringComparer.Ordinal);
    private bool _goonOptIn;
    private int _goonMediaGeneration, _goonPeerGeneration, _goonNoiseGeneration;
    // Per-window hooks let headless tests observe a deal without contacting the feed.
    internal Action<string, IReadOnlyList<string>>? GoonStartForTests;
    internal Action? GoonMoreForTests;

    internal object GoonMediaBlock()
    {
        var s = CoreSettings.Current;
        var stored = GoonOnlineMediaRules.CleanFlavour(s.GoonMediaFlavour);
        return new { flavour = _goonOptIn ? stored : "", last = stored,
            custom = GoonOnlineMediaRules.ParseCustom(s.GoonMediaCustom), online = s.GoonMediaOnline };
    }

    private bool HandleGoonMedia(JObject o)
    {
        if (_closing || _shellExiting) return true;
        var s = CoreSettings.Current;
        switch ((string?)o["type"])
        {
            case "media-flavour":
                _goonMediaGeneration++;
                var flavour = GoonOnlineMediaRules.CleanFlavour((string?)o["flavour"]);
                var subs = CleanGoonSubs(o);
                s.GoonMediaFlavour = flavour;
                if (o["custom"] is JObject) s.GoonMediaCustom = GoonOnlineMediaRules.CleanCustom(o["custom"]);
                s.GoonMediaSubs = GoonOnlineMediaRules.JoinSubs(subs);
                if (o["online"]?.Type == JTokenType.Boolean) s.GoonMediaOnline = (bool)o["online"]!;
                _goonOptIn = GoonOnlineMediaRules.IsSessionOptIn(s.GoonMediaOnline, flavour);
                try { CoreSettings.Save(); } catch { }
                if (!_goonOptIn) { _goonMedia?.Off(); return true; }
                EnsureGoonMedia();
                if (GoonStartForTests is { } start) start("online", subs);
                else _goonMedia!.Start(subs);
                return true;
            case "media-more":
                if (_goonOptIn && GoonOnlineMediaRules.ShouldFetch(s.GoonMediaOnline,
                    GoonOnlineMediaRules.CleanFlavour(s.GoonMediaFlavour), GoonOnlineMediaRules.SplitSubs(s.GoonMediaSubs)))
                {
                    if (GoonMoreForTests is { } more) more();
                    else _goonMedia?.More();
                }
                return true;
            case "peer-niches":
                _goonPeerGeneration++;
                var peer = CleanGoonSubs(o);
                EnsureGoonMedia();
                if (peer.Count == 0) _goonPeerMedia!.Off();
                else if (!GoonHostRules.PeerFetchAllowed(s.GoonMediaOnline))
                {
                    _goonPeerMedia!.Off();
                    Post(new { type = "peer-media", state = "declined", subs = Array.Empty<string>(), images = Array.Empty<object>(), videos = Array.Empty<object>() });
                }
                else if (GoonStartForTests is { } startPeer) startPeer("peer", peer);
                else _goonPeerMedia!.Start(peer);
                return true;
            case "noise-want":
                var set = o["set"]?.Type == JTokenType.String ? (string?)o["set"] : null;
                if (string.IsNullOrEmpty(set)) { ReleaseGoonNoise(); return true; }
                var sub = GoonNoiseSets.SubFor(set);
                if (sub == null) return true;
                if (!GoonNoiseSets.FetchAllowed(s.GoonMediaOnline, _goonOptIn, s.MediaSource, s.HasRemoteMediaConsent))
                {
                    Post(new { type = "noise-media", set, state = "declined", images = Array.Empty<object>() });
                    return true;
                }
                EnsureGoonMedia();
                if (!_goonNoise.TryGetValue(set, out var pool))
                {
                    pool = GoonOnlineMedia.ForNoise(set, snap => PostGoonSnapshot("noise-media", snap, set))!;
                    _goonNoise[set] = pool;
                }
                if (GoonStartForTests is { } startNoise) startNoise(set, new[] { sub });
                else pool.Start(new[] { sub });
                return true;
            default: return false;
        }
    }

    private static System.Collections.Generic.List<string> CleanGoonSubs(JObject o) => GoonOnlineMediaRules.CleanSubs(
        (o["subs"] as JArray)?.Select(t => t.Type == JTokenType.String ? (string?)t : null));

    private void EnsureGoonMedia()
    {
        GoonOnlineMedia.Materialize = DtrhRemoteMedia.AssetsTempCopier.Materialize;
        GoonOnlineMedia.ReleaseTempFile = DtrhRemoteMedia.AssetsTempCopier.Release;
        _goonMedia ??= new GoonOnlineMedia(s => PostGoonSnapshot("online-media", s));
        _goonPeerMedia ??= GoonOnlineMedia.ForPeer(s => PostGoonSnapshot("peer-media", s));
    }

    internal void PostGoonSnapshot(string type, GoonOnlineMedia.Snapshot snap, string? set = null)
    {
        var generation = type == "online-media" ? _goonMediaGeneration : type == "peer-media" ? _goonPeerGeneration : _goonNoiseGeneration;
        Dispatcher.UIThread.Post(() =>
        {
            var current = type == "online-media" ? _goonMediaGeneration : type == "peer-media" ? _goonPeerGeneration : _goonNoiseGeneration;
            if (_closing || _shellExiting || _server == null || generation != current) return;
            string Url(GoonOnlineMedia.Item item) => _server.AssetUrl(Path.GetRelativePath(CorePaths.EffectiveAssets, item.Path));
            var images = snap.Images.Select(i => new { name = i.Name, url = Url(i) }).ToArray();
            var videos = snap.Videos.Select(i => new { name = i.Name, url = Url(i) }).ToArray();
            if (set != null) Post(new { type, set, state = snap.State, images, progress = new { have = snap.Have, want = snap.Want } });
            else Post(new { type, state = snap.State, subs = snap.Subs, images, videos, progress = new { have = snap.Have, want = snap.Want } });
        });
    }

    private void ReleaseGoonNoise() { _goonNoiseGeneration++; foreach (var pool in _goonNoise.Values) pool.Dispose(); _goonNoise.Clear(); }
    private void DisposeGoonMedia() { _goonMedia?.Dispose(); _goonPeerMedia?.Dispose(); ReleaseGoonNoise(); }
}
