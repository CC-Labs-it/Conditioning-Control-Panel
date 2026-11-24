// WPF Services/GoonGame/GoonHostService.cs:1851-2072 (online pictures) and GoonShareCardHandler.cs.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Chaos;
using ConditioningControlPanel.Services.GoonGame;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Avalonia.Views.Games;

internal sealed partial class GameWindow
{
    // WPF :1848: the pick is this window's opt-in only, never app-wide consent.
    private GoonOnlineMedia? _goonOnline, _goonPeer;
    private readonly Dictionary<string, GoonOnlineMedia> _goonNoise = new(StringComparer.Ordinal);
    private bool _goonOptIn;

    private object GoonMediaBlock()
    {
        var s = CoreSettings.Current;
        var stored = GoonOnlineMediaRules.CleanFlavour(s.GoonMediaFlavour);
        return new { flavour = _goonOptIn ? stored : "", last = stored,
            custom = GoonOnlineMediaRules.ParseCustom(s.GoonMediaCustom), online = s.GoonMediaOnline };
    }

    private bool HandleGoonMedia(JObject o)
    {
        var s = CoreSettings.Current;
        // The head copier enforces the Scrolller licence, byte cap and temp prefix (as Pbp.Media.cs:101).
        GoonOnlineMedia.Materialize ??= DtrhRemoteMedia.AssetsTempCopier.Materialize;
        GoonOnlineMedia.ReleaseTempFile ??= DtrhRemoteMedia.AssetsTempCopier.Release;
        switch ((string?)o["type"])
        {
            case "media-flavour": // WPF :1866-1889
                var flavour = GoonOnlineMediaRules.CleanFlavour((string?)o["flavour"]);
                s.GoonMediaFlavour = flavour;
                if (o["custom"] is JObject) s.GoonMediaCustom = GoonOnlineMediaRules.CleanCustom(o["custom"]);
                s.GoonMediaSubs = GoonOnlineMediaRules.JoinSubs(GoonOnlineMediaRules.CleanSubs(Subs(o)));
                if (o["online"]?.Type == JTokenType.Boolean) s.GoonMediaOnline = (bool)o["online"]!;
                _goonOptIn = GoonOnlineMediaRules.IsSessionOptIn(s.GoonMediaOnline, flavour);
                try { CoreSettings.Save(); } catch (Exception ex) { Diag.Swallowed(ex); }
                if (_closing || !_goonOptIn) return true; // WPF :1891-1913
                var subs = GoonOnlineMediaRules.SplitSubs(s.GoonMediaSubs);
                _goonOnline ??= new GoonOnlineMedia(snap => PostGoonMedia("online-media", null, snap));
                if (!s.GoonMediaOnline) _goonOnline.Off();
                else if (GoonOnlineMediaRules.ShouldFetch(true, flavour, subs)) _goonOnline.Start(subs);
                else if (flavour != "") _goonOnline.Start(Array.Empty<string>());
                return true;
            case "media-more": // WPF :1917-1931
                if (_goonOptIn && GoonOnlineMediaRules.ShouldFetch(s.GoonMediaOnline,
                        GoonOnlineMediaRules.CleanFlavour(s.GoonMediaFlavour), GoonOnlineMediaRules.SplitSubs(s.GoonMediaSubs)))
                    _goonOnline?.More();
                return true;
            case "peer-niches": // WPF :1946-1968
                if (_closing) return true;
                var peer = GoonOnlineMediaRules.CleanSubs(Subs(o));
                _goonPeer ??= GoonOnlineMedia.ForPeer(snap => PostGoonMedia("peer-media", null, snap));
                if (peer.Count > 0 && GoonHostRules.PeerFetchAllowed(s.GoonMediaOnline)) { _goonPeer.Start(peer); return true; }
                _goonPeer.Off();
                if (peer.Count > 0) Post(new { type = "peer-media", state = "declined", subs = Array.Empty<string>(),
                    images = Array.Empty<object>(), videos = Array.Empty<object>() });
                return true;
            case "noise-want": // WPF :1981-2006
                if (_closing) return true;
                var set = o["set"]?.Type == JTokenType.String ? (string?)o["set"] : null;
                if (string.IsNullOrEmpty(set)) { ReleaseGoonNoise(); return true; }
                if (GoonNoiseSets.SubFor(set) is not { } sub) return true;
                if (!GoonNoiseSets.FetchAllowed(s.GoonMediaOnline, _goonOptIn, s.MediaSource, s.HasRemoteMediaConsent))
                { Post(new { type = "noise-media", set, state = "declined", images = Array.Empty<object>() }); return true; }
                if (!_goonNoise.TryGetValue(set, out var pool))
                {
                    if (GoonOnlineMedia.ForNoise(set, snap => PostGoonMedia("noise-media", set, snap)) is not { } made) return true;
                    _goonNoise[set] = pool = made;
                }
                pool.Start(new[] { sub });
                return true;
        }
        return false;
        static IEnumerable<string?>? Subs(JObject o) => (o["subs"] as JArray)?.Select(t => t.Type == JTokenType.String ? (string?)t : null);
    }

    // Worker thread -> page, with this head's token-gated loopback URLs (as Pbp.Media.cs:110).
    private void PostGoonMedia(string type, string? set, GoonOnlineMedia.Snapshot snap) => Dispatcher.UIThread.Post(() =>
    {
        if (_closing || _server == null) return;
        object Item(GoonOnlineMedia.Item i) => new { name = i.Name, url = _server.AssetUrl(Path.GetRelativePath(CorePaths.EffectiveAssets, i.Path)) };
        var progress = new { have = snap.Have, want = snap.Want };
        Post(set != null
            ? new { type, set, state = snap.State, images = snap.Images.Select(Item).ToList(), progress }
            : new { type, state = snap.State, subs = snap.Subs, images = snap.Images.Select(Item).ToList(),
                videos = snap.Videos.Select(Item).ToList(), progress } as object);
    });

    private void ReleaseGoonNoise()
    {
        foreach (var p in _goonNoise.Values) { try { p.Dispose(); } catch (Exception ex) { Diag.Swallowed(ex); } }
        _goonNoise.Clear();
    }

    private void StopGoonMedia()
    {
        try { _goonOnline?.Dispose(); _goonPeer?.Dispose(); } catch (Exception ex) { Diag.Swallowed(ex); }
        _goonOnline = _goonPeer = null;
        _goonOptIn = false;
        ReleaseGoonNoise();
    }

    // WPF GoonShareCardHandler.cs:16-101: PNG bytes only, one share-card-result per request.
    internal static Func<GameWindow, string, Task<string?>> GoonSavePicker = async (w, name) =>
    {
        var file = await w.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = name, DefaultExtension = ".png", ShowOverwritePrompt = true,
            SuggestedStartLocation = await w.StorageProvider.TryGetWellKnownFolderAsync(WellKnownFolder.Pictures),
            FileTypeChoices = new[] { new FilePickerFileType(Loc.Get("goon_share_png_filter")) { Patterns = new[] { "*.png" } } },
        });
        return file?.TryGetLocalPath();
    };

    private async Task ShareGoonCard(JObject o)
    {
        var req = GoonShareCard.Parse(o, out var error);
        var id = req?.Id ?? ((string?)o["id"] ?? "");
        void Reply(bool ok, string err) { if (!_closing) Post(new { type = "share-card-result", id, ok, error = err }); }
        if (req == null) { Reply(false, error); return; }
        try
        {
            using var bmp = new Bitmap(new MemoryStream(req.Png));
            if (bmp.PixelSize.Width > GoonShareCard.MaxPixels || bmp.PixelSize.Height > GoonShareCard.MaxPixels)
            { Reply(false, "bad-format"); return; }
            if (req.Action == "copy")
            {
                if (Clipboard is not { } clip) { Reply(false, "clipboard-busy"); return; }
                await clip.SetBitmapAsync(new Bitmap(new MemoryStream(req.Png)));
                Reply(true, "");
                return;
            }
            var path = await GoonSavePicker(this, req.FileName);
            if (path == null) { Reply(false, "cancelled"); return; }
            await File.WriteAllBytesAsync(path, req.Png);
            Reply(true, "");
        }
        catch (Exception ex) { Serilog.Log.Warning("[Goon] share-card {A} failed: {E}", req.Action, ex.Message); Reply(false, "failed"); }
    }
}
