// Adapted from parity/trunk 2d3523b2b0078ce8ebb3ccca88dbb2cef7228724 and WPF Media.cs (7.1.5).
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Fyp.Online;
using ConditioningControlPanel.Services.GoonGame.Online;
using ConditioningControlPanel.Services.PieceByPiece.Media;
using Newtonsoft.Json.Linq;
namespace ConditioningControlPanel.Avalonia.Views.Games;
internal sealed partial class GameWindow
{
    private GoonOnlineMedia? _pbpOnline;
    internal Task PbpOnlinePending => _pbpOnline?.Pending ?? Task.CompletedTask;
    private bool _pbpSessionOptIn;
    private int _pbpSharePct;
    private bool? _pbpLocalMedia, _pbpPostedAvailable;
    private string? _pbpOnlineState;
    internal Task PbpProbe { get; private set; } = Task.CompletedTask;
    private void StartPbpProbe()
    {
        var (server, assets, disabled) = (_server, CorePaths.EffectiveAssets, CoreSettings.Current.DisabledAssetPaths?.ToArray());
        _pbpLibrary = new(() => PbpLibrary(server, assets, disabled).ToArray());
        PbpProbe = Task.Run(() =>
        {
            bool found = false;
            try { found = PbpMediaRules.FolderHasEntries(assets) && _pbpLibrary.Value.Length > 0; }
            catch (Exception ex) { Diag.Swallowed(ex); }
            Dispatcher.UIThread.Post(() => { if (!_closing) { _pbpLocalMedia = found; RefreshPbpAvailable(); } });
        });
    }
    private System.Collections.Generic.List<string> PbpChannels()
    {
        var s = CoreSettings.Current;
        return PbpMediaRules.ChannelsFor(s.PbpMediaOnline, _pbpSessionOptIn, s.MediaSource, s.HasRemoteMediaConsent,
            s.PbpMediaFlavour, GoonOnlineMediaRules.SplitSubs(s.PbpMediaSubs),
            FypOnlineCoordinator.ResolveChannels(s.FypOnlineNiches, s.FypOnlineCustomSubs));
    }
    private bool PbpMediaAvailable() => PbpMediaRules.MediaAvailable(_pbpLocalMedia ?? true, PbpChannels(), _pbpOnlineState);
    private void RefreshPbpAvailable()
    {
        if (IsReady && _pbpPostedAvailable != null && PbpMediaAvailable() != _pbpPostedAvailable) PostPbpMediaState();
    }
    private void PostPbpMediaState()
    {
        var s = CoreSettings.Current;
        var stored = GoonOnlineMediaRules.CleanFlavour(s.PbpMediaFlavour);
        bool appWide = PbpMediaRules.AppWideOnline(s.MediaSource, s.HasRemoteMediaConsent);
        _pbpPostedAvailable = PbpMediaAvailable();
        Post(new { type = "pbp:media-state", flavour = _pbpSessionOptIn || appWide ? stored : "", last = stored,
            custom = GoonOnlineMediaRules.ParseCustom(s.PbpMediaCustom), online = s.PbpMediaOnline, appWide,
            chosen = PbpMediaRules.HasSavedChoice(s.PbpMediaChosen, s.PbpMediaOnline, stored),
            library = true, canOnline = true, mediaAvailable = _pbpPostedAvailable.Value });
    }
    private void OnPbpMediaFlavour(JObject o)
    {
        var s = CoreSettings.Current;
        s.PbpMediaFlavour = GoonOnlineMediaRules.CleanFlavour(o["flavour"]?.Type == JTokenType.String ? (string?)o["flavour"] : null);
        if (o["custom"] is JObject) s.PbpMediaCustom = GoonOnlineMediaRules.CleanCustom(o["custom"]);
        s.PbpMediaSubs = GoonOnlineMediaRules.JoinSubs(GoonOnlineMediaRules.CleanSubs(
            (o["subs"] as JArray)?.Select(t => t.Type == JTokenType.String ? (string?)t : null)));
        if (o["online"]?.Type == JTokenType.Boolean) s.PbpMediaOnline = (bool)o["online"]!;
        s.PbpMediaChosen = true;
        _pbpSessionOptIn = PbpMediaRules.SavedOptIn(s.PbpMediaOnline, s.PbpMediaFlavour);
        _pbpOnlineState = null;
        CoreSettings.Save();
        PostPbpMediaState();
        StartPbpOnlineMedia();
    }
    private void StartPbpOnlineMedia()
    {
        var s = CoreSettings.Current;
        _pbpSharePct = PbpMediaRules.SharePct(s.MediaSource, s.RemoteMediaRatio, _pbpSessionOptIn);
        var subs = PbpChannels();
        if (subs.Count == 0)
        {
            if (_pbpOnline != null) _pbpOnline.Off();
            else PostPbpOnlineMedia(new("off", Array.Empty<string>(), Array.Empty<GoonOnlineMedia.Item>(), Array.Empty<GoonOnlineMedia.Item>(), 0, 0));
            return;
        }
        _pbpOnline ??= GoonOnlineMedia.ForGame("pbp", PostPbpOnlineMedia);
        _pbpOnline.Start(subs);
    }
    private void PostPbpOnlineMedia(GoonOnlineMedia.Snapshot snap)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_closing) return;
            string Url(GoonOnlineMedia.Item i) => _server!.AssetUrl(Path.GetRelativePath(CorePaths.EffectiveAssets, i.Path));
            Post(new { type = "pbp:online-media", state = snap.State, subs = snap.Subs, share = _pbpSharePct,
                images = snap.Images.Select(Url).ToArray(), clips = snap.Videos.Select(Url).ToArray(), have = snap.Have, want = snap.Want });
            _pbpOnlineState = snap.State;
            RefreshPbpAvailable();
        });
    }
}
