using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Services.GoonGame;
using ConditioningControlPanel.Services.Safety;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Transfer;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Avalonia.Views.Games;

internal sealed partial class GameWindow
{
    // WPF GoonHostService.cs:290: the Play door opens the hosted Goon page.
    static partial void RegisterGoon(Dictionary<string, Game> games) =>
        games["goon"] = new("goon", "launcher_game_goon_title", "goon/index.html");

    // WPF GoonHostService.cs:210,1008: one client, 40 second long-poll timeout.
    private static readonly Lazy<HttpClient> GoonHttp = new(() =>
    {
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(40) };
        try { client.DefaultRequestHeaders.UserAgent.ParseAdd("ConditioningControlPanel/" + CoreReleaseContent.AppVersion); }
        catch (Exception ex) { Diag.Swallowed(ex); }
        return client;
    });
    internal static Func<HttpClient> GoonHttpClient = () => GoonHttp.Value;

    // WPF GoonHostService.cs:1667-1782: a live page must keep beating and painting.
    internal static Func<DateTime> GoonUtcNow = () => DateTime.UtcNow;
    private DispatcherTimer? _goonWatch;
    private DateTime _goonBeat, _goonPaintMove, _goonTick;
    private long? _goonPaint;
    private bool _goonRetried;
    private static int _goonPanicEpoch;

    private void StartGoonWatch()
    {
        _goonBeat = _goonPaintMove = _goonTick = GoonUtcNow();
        GoonCacheBridge.Attach(PostGoonCache);
        _goonWatch = new DispatcherTimer { Interval = TimeSpan.FromSeconds(GoonHostRules.WatchIntervalSeconds) };
        _goonWatch.Tick += (_, _) => CheckGoonWatch(GoonUtcNow());
        _goonWatch.Start();
    }

    partial void HandleGoonMessage(JObject message, ref bool handled)
    {
        if (HandleGoonInbox(message)) { handled = true; return; }
        switch ((string?)message["type"])
        {
            case "cache-req":
            case "cache-put":
            case "encode-done":
                if (!_closing) GoonCacheBridge.OnMessage(message);
                handled = true;
                break;
            case "ready":
                if (_closing) { handled = true; return; }
                IsReady = true;
                var consent = new ConsentSheetMsg();
                Post(new { type = "init", protocol = 1, lang = CoreSettings.Current.Language,
                    identity = new { unifiedId = CoreAccount.UnifiedUserId ?? "",
                        displayName = CoreAccount.DisplayName ?? "", appVersion = CoreReleaseContent.AppVersion },
                    net = new { serverBase = GoonSignalingClient.ProxyBaseUrl, authToken = PbpAuthToken(), viaHost = true },
                    caps = new { haptics = false, brainDrain = false, spiral = true, camera = false,
                        video = true, mediaTransfer = false, canHost = GoonHostRules.HostingAllowed(), canJoin = GoonHostRules.JoiningAllowed() },
                    consent = new { liveDurationSec = consent.LiveDurationSec, toyCap = consent.ToyCap,
                        payloadMinGapMs = consent.PayloadMinGapMs }, fullscreen = WindowState == global::Avalonia.Controls.WindowState.FullScreen });
                BootGoonInbox();
                _goonBeat = _goonPaintMove = _goonTick = GoonUtcNow();
                _goonPaint = null;
                handled = true;
                break;
            case "net-post":
                if (!_closing) RelayGoonPost(message);
                handled = true;
                break;
            case "heartbeat":
            case "pong":
                var now = GoonUtcNow();
                _goonBeat = now;
                var vis = (string?)message["vis"];
                if (!string.IsNullOrEmpty(vis) && vis != "visible") _goonPaintMove = now;
                else if (message["paint"]?.Type == JTokenType.Integer && long.TryParse(message["paint"]!.ToString(), out var paint) && paint != _goonPaint)
                {
                    _goonPaint = paint;
                    _goonPaintMove = now;
                }
                handled = true;
                break;
        }
    }

    // WPF GoonHostService.cs:1020-1087: only the built Goon URI may carry the account token.
    private void RelayGoonPost(JObject message)
    {
        var id = (string?)message["id"] ?? "";
        var path = (string?)message["path"] ?? "";
        var generation = Volatile.Read(ref _goonInboxGeneration);
        void Reply(int status, string body) => Dispatcher.UIThread.Post(() =>
        {
            if (!_closing && generation == Volatile.Read(ref _goonInboxGeneration))
                Post(new { type = "net-post-result", id, status, body });
        });
        if (!HostNetPathRule.TryBuild(GoonSignalingClient.ProxyBaseUrl, path, "/v2/goon/", out var target) || target == null)
        {
            Reply(0, "forbidden_path");
            return;
        }
        var body = message["body"]?.Type == JTokenType.String ? (string?)message["body"] ?? ""
            : message["body"]?.ToString(Newtonsoft.Json.Formatting.None) ?? "";
        _ = Task.Run(async () =>
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, target)
                { Content = new StringContent(body, Encoding.UTF8, "application/json") };
                var token = PbpAuthToken();
                if (!string.IsNullOrEmpty(token)) request.Headers.Add("X-Auth-Token", token);
                request.Headers.Add("X-Client-Version", CoreReleaseContent.AppVersion);
                using var response = await GoonHttpClient().SendAsync(request).ConfigureAwait(false);
                var result = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                MergedAccountRecovery.TryHandle((int)response.StatusCode, result);
                Reply((int)response.StatusCode, result);
            }
            catch (Exception ex) { Serilog.Log.Debug("[Goon] net-post failed: {Error}", ex.Message); Reply(0, ""); }
        });
    }

    internal void CheckGoonWatch(DateTime now)
    {
        var gap = (now - _goonTick).TotalSeconds;
        _goonTick = now;
        if (_closing || _shellExiting || !IsReady) return;
        var silent = (now - _goonBeat).TotalSeconds;
        if (silent > 20) { RecoverGoon(); return; }
        if (_goonPaint != null)
        {
            var verdict = GoonHostRules.PaintStallVerdict((now - _goonPaintMove).TotalSeconds, gap, 0);
            if (verdict == GoonHostRules.PaintStallCall.UiStalled) _goonPaintMove = now;
            else if (verdict == GoonHostRules.PaintStallCall.Stalled) { RecoverGoon(); return; }
        }
        if (silent > 8) Post(new { type = "ping" });
    }

    private void RecoverGoon()
    {
        var retry = !_goonRetried;
        var epoch = _goonPanicEpoch;
        Close();
        if (!retry || epoch != _goonPanicEpoch) return;
        var next = LaunchWindow(Spec);
        if (next != null) _goonRetried = next._goonRetried = true;
    }

    private void PostGoonCache(object frame) => Dispatcher.UIThread.Post(() =>
    {
        if (!_closing) Post(frame);
    });

    private void StopGoonWatch()
    {
        // WPF GoonHostService.cs:2113-2118: a closed match must not strand the queue.
        try { GoonCacheBridge.Detach(); } catch (Exception ex) { Diag.Swallowed(ex); }
        try { TransferCompressionService.Instance.ResumeAfterMatch(); } catch (Exception ex) { Diag.Swallowed(ex); }
        _goonWatch?.Stop();
        _goonWatch = null;
    }
}
