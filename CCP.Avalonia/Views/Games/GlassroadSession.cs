using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Services.PieceByPiece;
using ConditioningControlPanel.Services.Safety;
using Newtonsoft.Json.Linq;
using Serilog;
namespace ConditioningControlPanel.Avalonia.Views.Games;
// Both transports execute this session, including the signed relay and watchdog.
internal sealed class GlassroadSession : IDisposable
{
    private readonly Action<object> Post;
    private readonly Action Close, _focus;
    private bool _grOpen = true, _closing, _grEarlyInitSent, _grPinged;
    private DateTime _grLastBeatUtc = DateTime.UtcNow, _grLastProgressUtc = DateTime.UtcNow;
    internal bool Ready { get; private set; }
    internal Task GlassroadPending { get; private set; } = Task.CompletedTask;
    internal GlassroadSession(Action<object> post, Action close, Action focus)
    {
        Post = post; Close = close; _focus = focus;
        try { FriendsHead.Service?.EnterActivity(PresenceActivity.Race); } catch { }
    }
    internal void PostGlassroadInit()
    {
        var s = CoreSettings.Current;
        var uid = CoreAccount.UnifiedUserId;
        var online = !string.IsNullOrEmpty(uid) && !string.IsNullOrEmpty(GameWindow.PbpAuthToken());
        Post(GlassroadHostRules.InitFrame(online ? uid : null, s.UserDisplayName,
            s.LastKnownEffectiveTier ?? (CoreEntitlement.HasLab ? 2 : CoreEntitlement.HasPremium ? 1 : 0),
            GentleVisuals.On, s.GentleFadeSeconds, s.GentleSpeed));
    }
    public void Dispose()
    {
        if (_closing) return;
        _closing = true; _grOpen = false;
        try { FriendsHead.Service?.LeaveActivity(PresenceActivity.Race); } catch { }
    }
        internal bool HandleGlassroad(JObject o)
        {
            _grLastProgressUtc = DateTime.UtcNow;
            var type = (string?)o["type"];
            try
            {
                if (!_grEarlyInitSent && !Ready && type != "ready")
                {
                    _grEarlyInitSent = true;
                    PostGlassroadInit();   // backstop for a load-time init that beat the bridge
                }
                switch (type)
                {
                    case "ready":
                        Ready = true;
                        _grLastBeatUtc = DateTime.UtcNow;
                        _grPinged = false;
                        PostGlassroadInit();
                        _focus();
                        return true;
                    case "heartbeat":
                    case "pong":
                        _grLastBeatUtc = DateTime.UtcNow;
                        _grPinged = false;
                        return true;
                    case "net":
                        OnGlassroadNet(o);
                        return true;
                    case "exit":
                    case "menu-back":
                        // The page's own Leave / the menu's Back link. Escape never sends these.
                        Close();
                        return true;
                }
            }
            catch (Exception ex)
            {
                Log.Warning("Glassroad: message handler threw ({Type}): {E}", type, ex.Message);
                return true;
            }
            return false;
        }
        private void OnGlassroadNet(JObject o)
        {
            var id = (string?)o["id"] ?? "";
            var path = (string?)o["path"] ?? "";
            var method = ((string?)o["method"] ?? "GET").Trim().ToUpperInvariant();
            var b = o["body"];
            var body = b == null || b.Type == JTokenType.Null ? ""
                : b.Type == JTokenType.String ? (string?)b ?? "" : b.ToString(Newtonsoft.Json.Formatting.None);
            if (method != "GET" && method != "POST") { ReplyGlassroadNet(id, 0, "forbidden_method"); return; }
            var baseUrl = GameWindow.PbpServerBase();
            if (string.IsNullOrEmpty(baseUrl)) { ReplyGlassroadNet(id, 0, ""); return; }   // a sandbox with no loopback
            // Signed first (the host names the caller), then the SIGNED path is judged on its built Uri.
            if (!GlassroadHostRules.TrySign(method, path, body, CoreAccount.UnifiedUserId, out var signedPath, out var signedBody)
                || !HostNetPathRule.TryBuild(baseUrl, signedPath, GlassroadHostRules.AllowedPathPrefix, out var target)
                || target is null)
            {
                Log.Warning("Glassroad: net REJECTED for path '{Path}'", path);
                ReplyGlassroadNet(id, 0, "forbidden_path");
                return;
            }
            var token = GameWindow.PbpAuthToken();
            var http = GameWindow.PbpHttpClient();
            GlassroadPending = Task.Run(async () =>
            {
                int status = 0;
                string reply = "";
                try
                {
                    using var request = new HttpRequestMessage(method == "POST" ? HttpMethod.Post : HttpMethod.Get, target);
                    if (method == "POST") request.Content = new StringContent(signedBody, Encoding.UTF8, "application/json");
                    if (!string.IsNullOrEmpty(token)) request.Headers.Add("X-Auth-Token", token);
                    request.Headers.Add("X-Client-Version", CoreReleaseContent.AppVersion);
                    using var response = await http.SendAsync(request).ConfigureAwait(false);
                    status = (int)response.StatusCode;
                    reply = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    (status, reply) = (0, "");
                    Log.Debug("Glassroad: net {Method} {Path} failed: {E}", method, path, ex.Message);
                }
                Dispatcher.UIThread.Post(() => ReplyGlassroadNet(id, status, reply));
            });
        }
        private void ReplyGlassroadNet(string id, int status, string body)
            { if (!_closing) Post(new { type = "net-reply", id, status, body }); }
        internal string CheckGlassroadWatch(DateTime nowUtc)
        {
            if (!_grOpen || _closing) return "idle";
            if (PbpHostRules.BootTimedOut(Ready, nowUtc - _grLastProgressUtc))
            {
                Log.Warning("Glassroad: no page progress for {S:0}s - closing", PbpHostRules.BootDeadline.TotalSeconds);
                Close();
                return "boot-timeout";
            }
            switch (PbpHostRules.HeartbeatStep(Ready, (nowUtc - _grLastBeatUtc).TotalSeconds, _grPinged))
            {
                case PbpHostRules.WatchStep.Ping:
                    _grPinged = true;
                    Post(new { type = "ping", t = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() });
                    return "ping";
                case PbpHostRules.WatchStep.Close:
                    Log.Warning("Glassroad: page heartbeat silent >20s and no pong - closing");
                    Close();
                    return "closed";
                case PbpHostRules.WatchStep.Ok:
                    return "ok";
                default:
                    return "idle";
            }
        }
}
