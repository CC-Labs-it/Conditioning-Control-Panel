// WPF Services/PieceByPiece/PieceByPieceHostService.Media.cs:37-233.
using System;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Services.Chaos;
using ConditioningControlPanel.Services.Fyp.Online;
using ConditioningControlPanel.Services.GoonGame;
using ConditioningControlPanel.Services.PieceByPiece;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Avalonia.Views.Games;

internal sealed partial class GameWindow
{
    private GoonOnlineMedia? _pbpOnline;
    private bool _pbpSessionOptIn;
    private bool? _pbpLocalMedia, _pbpPostedAvailable;
    private string? _pbpOnlineState;
    private int _pbpShare, _pbpMediaGeneration;
    internal Task PbpLocalProbe { get; private set; } = Task.CompletedTask;

    // WPF Media.cs:113-136. Each window owns its answer; a closed window drops late work.
    private void StartPbpLocalProbe()
    {
        var (server, assets, disabled) = (_server, CorePaths.EffectiveAssets, CoreSettings.Current.DisabledAssetPaths?.ToArray());
        _pbpLibrary = new(() => PbpLibrary(server, assets, disabled).ToArray());
        var library = _pbpLibrary;
        PbpLocalProbe = Task.Run(() =>
        {
            bool found;
            try { found = PbpMediaRules.FolderHasEntries(assets) && library.Value.Length > 0; }
            catch { found = false; }
            Dispatcher.UIThread.Post(() =>
            {
                if (_closing) return;
                _pbpLocalMedia = found;
                RefreshPbpMediaAvailable();
            });
        });
    }

    private System.Collections.Generic.List<string> PbpChannels()
    {
        var s = CoreSettings.Current;
        return PbpMediaRules.ChannelsFor(s.PbpMediaOnline, _pbpSessionOptIn, s.MediaSource,
            s.HasRemoteMediaConsent, s.PbpMediaFlavour, GoonOnlineMediaRules.SplitSubs(s.PbpMediaSubs),
            FypOnlineCoordinator.ResolveChannels(s.FypOnlineNiches, s.FypOnlineCustomSubs));
    }

    private bool PbpMediaAvailable() => PbpMediaRules.MediaAvailable(_pbpLocalMedia ?? true, PbpChannels(), _pbpOnlineState);

    // WPF Media.cs:47-69: active pick, stored preselection and one-time choice are distinct.
    private void PostPbpMediaState()
    {
        var s = CoreSettings.Current;
        var stored = GoonOnlineMediaRules.CleanFlavour(s.PbpMediaFlavour);
        var appWide = PbpMediaRules.AppWideOnline(s.MediaSource, s.HasRemoteMediaConsent);
        _pbpPostedAvailable = PbpMediaAvailable();
        Post(new { type = "pbp:media-state", flavour = _pbpSessionOptIn || appWide ? stored : "",
            last = stored, custom = GoonOnlineMediaRules.ParseCustom(s.PbpMediaCustom),
            online = s.PbpMediaOnline, appWide,
            chosen = PbpMediaRules.HasSavedChoice(s.PbpMediaChosen, s.PbpMediaOnline, stored),
            library = true, canOnline = true, mediaAvailable = _pbpPostedAvailable.Value });
    }

    private void RefreshPbpMediaAvailable()
    {
        if (IsReady && _pbpPostedAvailable != null && PbpMediaAvailable() != _pbpPostedAvailable)
            PostPbpMediaState();
    }

    // WPF Media.cs:77-95. A game pick never grants app-wide remote consent.
    private void OnPbpMediaFlavour(JObject o)
    {
        var s = CoreSettings.Current;
        s.PbpMediaFlavour = GoonOnlineMediaRules.CleanFlavour((string?)o["flavour"]);
        if (o["custom"] is JObject) s.PbpMediaCustom = GoonOnlineMediaRules.CleanCustom(o["custom"]);
        s.PbpMediaSubs = GoonOnlineMediaRules.JoinSubs(GoonOnlineMediaRules.CleanSubs(
            (o["subs"] as JArray)?.Select(t => t.Type == JTokenType.String ? (string?)t : null)));
        if (o["online"]?.Type == JTokenType.Boolean) s.PbpMediaOnline = (bool)o["online"]!;
        s.PbpMediaChosen = true;
        _pbpSessionOptIn = GoonOnlineMediaRules.IsSessionOptIn(s.PbpMediaOnline, s.PbpMediaFlavour);
        _pbpOnlineState = null;
        try { CoreSettings.Service?.Save(); } catch (Exception ex) { Diag.Swallowed(ex); }
        PostPbpMediaState();
        StartPbpOnline();
    }

    // WPF Media.cs:179-198. Reuse the bounded pool, its cancellation and ownership.
    private void StartPbpOnline()
    {
        _pbpMediaGeneration++;
        var s = CoreSettings.Current;
        _pbpShare = PbpMediaRules.SharePct(s.MediaSource, s.RemoteMediaRatio, _pbpSessionOptIn);
        var channels = PbpChannels();
        if (channels.Count == 0)
        {
            if (_pbpOnline != null) _pbpOnline.Off();
            else PostPbpOnline(new("off", Array.Empty<string>(), Array.Empty<GoonOnlineMedia.Item>(), Array.Empty<GoonOnlineMedia.Item>(), 0, 0));
            return;
        }
        // Existing head copier enforces the Scrolller licence/cache, byte cap and temp prefix.
        GoonOnlineMedia.Materialize ??= DtrhRemoteMedia.AssetsTempCopier.Materialize;
        GoonOnlineMedia.ReleaseTempFile ??= DtrhRemoteMedia.AssetsTempCopier.Release;
        _pbpOnline ??= GoonOnlineMedia.ForGame("pbp", PostPbpOnline);
        _pbpOnline.Start(channels);
    }

    // WPF Media.cs:205-224. URLs use this head's authenticated loopback asset mapping.
    internal void PostPbpOnline(GoonOnlineMedia.Snapshot snap)
    {
        var share = _pbpShare;
        var generation = _pbpMediaGeneration;
        Dispatcher.UIThread.Post(() =>
        {
            if (_closing || _server == null || generation != _pbpMediaGeneration) return;
            string Url(GoonOnlineMedia.Item item) => _server.AssetUrl(Path.GetRelativePath(CorePaths.EffectiveAssets, item.Path));
            Post(new { type = "pbp:online-media", state = snap.State, subs = snap.Subs, share,
                images = snap.Images.Select(Url).ToArray(), clips = snap.Videos.Select(Url).ToArray(),
                have = snap.Have, want = snap.Want });
            _pbpOnlineState = snap.State;
            RefreshPbpMediaAvailable();
        });
    }
}
