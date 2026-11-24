using System;
using System.Collections.Generic;
using Avalonia.Threading;
using ConditioningControlPanel.Services.GoonGame;
using ConditioningControlPanel.Services.Transfer;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Avalonia.Views.Games;

internal sealed partial class GameWindow
{
    // WPF GoonHostService.cs:290: the Play door opens the hosted Goon page.
    static partial void RegisterGoon(Dictionary<string, Game> games) =>
        games["goon"] = new("goon", "launcher_game_goon_title", "goon/index.html");

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
                    practiceOnly = true,
                    net = new { viaHost = true },
                    caps = new { haptics = false, brainDrain = false, spiral = true, camera = false,
                        video = true, mediaTransfer = false, canHost = false, canJoin = false },
                    consent = new { liveDurationSec = consent.LiveDurationSec, toyCap = consent.ToyCap,
                        payloadMinGapMs = consent.PayloadMinGapMs }, fullscreen = WindowState == global::Avalonia.Controls.WindowState.FullScreen });
                BootGoonInbox();
                _goonBeat = _goonPaintMove = _goonTick = GoonUtcNow();
                _goonPaint = null;
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
