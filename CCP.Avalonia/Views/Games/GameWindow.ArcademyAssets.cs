// WPF ArcademyHostService.cs:4626-4663, 4835-4864, 4909-4956.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Services.Fyp;
using ConditioningControlPanel.Services.Fyp.Online;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Games;

internal sealed partial class GameWindow
{
    private static readonly Dictionary<string, IReadOnlyList<string>> ArcademyTaggedSubs = new(StringComparer.Ordinal);
    internal Func<FeedMediaKind, IReadOnlyList<string>, Task<List<FypAssetManifest.Entry>>>? ArcademyFetchForTests;
    // WPF RemoteBuffer (ArcademyHostService.cs:106): a batch's surplus rows wait here for the next ask. Per window, so a close clears it (WPF :6078).
    private readonly Dictionary<string, List<object>> _arcademyAssetBuffer = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string[]> _arcademyPileSubs = new(StringComparer.Ordinal);
    private static bool ArcademyRemoteAllowed() => !CoreSettings.Current.OfflineMode
        && CoreSettings.Current.MediaSource != "local" && CoreSettings.Current.HasRemoteMediaConsent;

    private void PostArcademyAssetsUnavailable(JObject message)
    {
        string id = message["reqId"]?.Type == JTokenType.String ? (string?)message["reqId"] ?? "" : "";
        string kind = (string?)message["kind"] == "loop" ? "loop" : "still";
        int count;
        try { count = message["count"]?.Type == JTokenType.Integer ? Math.Clamp((int)message["count"]!, 1, 24) : 8; }
        catch { count = 8; }
        bool tagged = message["subs"] != null && message["subs"]!.Type != JTokenType.Null;
        string tag = ArcademyTag(message);
        var subs = tagged ? (message["subs"] as JArray ?? new JArray()).Where(t => t.Type == JTokenType.String)
            .Select(t => FypOnlineCoordinator.SanitizeSub((string?)t)).Where(x => x != null).Select(x => x!)
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(64).ToArray()
            : FypOnlineCoordinator.ResolveChannels(CoreSettings.Current.FypOnlineNiches, CoreSettings.Current.FypOnlineCustomSubs).ToArray();
        void Reply(object[] urls, bool done)
        {
            if (tagged) Post(new { type = "assets", reqId = id, tag, urls, done });
            else Post(new { type = "assets", reqId = id, urls, done });
        }
        if (!ArcademyRemoteAllowed() || (tagged && subs.Length == 0)) { Reply(Array.Empty<object>(), true); return; }
        string key = tagged ? "sort:" + tag + "|" + kind : kind;
        if (tagged && (!_arcademyPileSubs.TryGetValue(tag, out var pile) || !pile.SequenceEqual(subs, StringComparer.OrdinalIgnoreCase)))
        {
            // A pile pointed at new subs drops its buffered rows: their src names the old subs (WPF :4885-4896).
            _arcademyAssetBuffer.Remove("sort:" + tag + "|loop");
            _arcademyAssetBuffer.Remove("sort:" + tag + "|still");
        }
        if (tagged) _arcademyPileSubs[tag] = subs;
        var served = TakeArcademyBuffered(key, count);
        if (served.Length >= count) { Reply(served, true); return; }
        Reply(served, false);
        int want = count - served.Length;
        _ = Task.Run(async () =>
        {
            var mediaKind = kind == "loop" ? FeedMediaKind.Video : FeedMediaKind.Image;
            var entries = new List<FypAssetManifest.Entry>();
            try
            {
                if (ArcademyFetchForTests is { } fetch) entries = await fetch(mediaKind, subs);
                else
                {
                    string tenant = tagged ? "arcademy:sort:" + tag : "arcademy";
                    bool changed;
                    lock (ArcademyTaggedSubs)
                    {
                        changed = !ArcademyTaggedSubs.TryGetValue(tenant, out var old) || !old.SequenceEqual(subs, StringComparer.OrdinalIgnoreCase);
                        ArcademyTaggedSubs[tenant] = subs;
                    }
                    var coordinator = FypOnlineCoordinator.For(tenant, () => { lock (ArcademyTaggedSubs) return ArcademyTaggedSubs.GetValueOrDefault(tenant) ?? Array.Empty<string>(); }, FeedMediaKind.Any);
                    if (changed) coordinator.ResetChannels();
                    (entries, _) = await coordinator.FetchBatchAsync(mediaKind, CancellationToken.None);
                }
            }
            catch (Exception ex) { Log.Debug(ex, "[Arcademy] remote assets unavailable"); }
            var allowed = new HashSet<string>(subs, StringComparer.OrdinalIgnoreCase);
            var rows = entries.Where(e => RemoteMediaFormats.Validate(e, mediaKind, out _) && (!tagged || allowed.Contains((e.Folder ?? "").StartsWith("r/", StringComparison.OrdinalIgnoreCase) ? e.Folder[2..] : e.Folder ?? "")))
                .Take(24).Select(e =>   // WPF RemoteBatchCap (:60)
                {
                    var url = e.SmallUrl;
                    if (!Uri.TryCreate(url, UriKind.Absolute, out var small) || (small.Scheme != Uri.UriSchemeHttp && small.Scheme != Uri.UriSchemeHttps)
                        || (kind == "loop" ? !RemoteMediaFormats.IsRemoteVideo(url) : !RemoteMediaFormats.IsRemoteImage(url))) url = e.Url;
                    string ext = Path.GetExtension(new Uri(url).AbsolutePath).ToLowerInvariant();
                    string mime = ext switch { ".webm" => "video/webm", ".mp4" => "video/mp4", ".png" => "image/png", ".webp" => "image/webp", _ => "image/jpeg" };
                    var src = (e.Folder ?? "").StartsWith("r/", StringComparison.OrdinalIgnoreCase) ? e.Folder : "r/" + e.Folder;
                    var poster = kind == "loop" ? e.PosterUrl : null;
                    return tagged ? (object)new { url, kind, mime, tag, src, poster }
                        : new { url, kind, mime, poster };
                }).ToArray();
            Dispatcher.UIThread.Post(() =>
            {
                if (_closing) return;
                if (!ArcademyRemoteAllowed()) { Reply(Array.Empty<object>(), true); return; }
                // A batch for subs the pile has since left is still this request's answer, but not the pile's stock.
                if (!tagged || (_arcademyPileSubs.TryGetValue(tag, out var now) && now.SequenceEqual(subs, StringComparer.OrdinalIgnoreCase)))
                {
                    if (!_arcademyAssetBuffer.TryGetValue(key, out var buf)) _arcademyAssetBuffer[key] = buf = new List<object>();
                    buf.AddRange(rows.Skip(want));
                    if (buf.Count > 120) buf.RemoveRange(0, buf.Count - 120);
                }
                Reply(rows.Take(want).ToArray(), true);
            });
        });
    }

    private object[] TakeArcademyBuffered(string key, int count)
    {
        if (!_arcademyAssetBuffer.TryGetValue(key, out var buf)) return Array.Empty<object>();
        var taken = buf.Take(count).ToArray();
        buf.RemoveRange(0, taken.Length);
        return taken;
    }
}
