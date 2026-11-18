using System;
using System.Linq;
using System.IO;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.BackRoom;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.BackRoom;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Avalonia.Views.Games;

internal sealed partial class GameWindow
{
    private BackRoomFx? _backRoomFx;
    private bool _roomMinimized;
    internal void RoomMinimizedChanged(bool minimized)
    {
        if (_roomMinimized == minimized) return;
        _roomMinimized = minimized;
        if (minimized) { _backRoomFx?.CancelAll(); if (IsBreakout) StopBreakoutHaptic(); }
        Post(new { type = "suspend", on = minimized, reason = "minimise" });
    }
    internal Func<BackRoomRainBridge> RainBridgeFactory = () => new BackRoomRainBridge();
    internal Func<BackRoomFlashBridge>? FlashBridgeFactory;
    internal Func<BackRoomMediaDeal> RoomFxDealFactory = () => new BackRoomMediaDeal(0, Array.Empty<BackRoomGif>(), Array.Empty<BackRoomWord>());
    internal Func<IFxScheduler> RoomFxSchedulerFactory = () => new RainScheduler();

    private BackRoomFx RoomFx => _backRoomFx ??= new BackRoomFx(new RainSink(RainBridgeFactory(), FlashBridgeFactory?.Invoke() ??
        new BackRoomFlashBridge(this, () => Chaos.ChaosRunHost.IsDescending, CorePaths.EffectiveAssets,
            Path.Combine(AppContext.BaseDirectory, "Resources", "web"))),
        RoomFxSchedulerFactory(), () => new FxEnvironment(global::ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env.Level,
            CoreSettings.Current.BackRoomFxIntensity), supports: p => p is FxPrim.GifRain or FxPrim.GlitchBubbles or FxPrim.GifFull);

    private void FireRoomFx(JObject message)
    {
        var token = (string?)message["token"];
        var id = (string?)message["fxId"] ?? "";
        var station = (string?)message["station"] ?? "";
        var symbols = (message["symbols"] as JArray)?.Where(s => s.Type == JTokenType.String).Select(s => (string)s!).ToArray() ?? Array.Empty<string>();
        var deal = RoomFxDealFactory();
        var ack = RoomFx.Fire(id, station, symbols, deal, BackRoomFxArgs.Parse(message["args"]), token is { Length: <= 64 } ? token : null);
        Post(new { type = "fx-ack", token, fired = ack.Fired,
            skipped = ack.Skipped.Select(s => new { prim = s.Prim, why = s.Why.ToString().ToLowerInvariant() }) });
    }

    private sealed class RainScheduler : IFxScheduler
    {
        public long NowMs => Environment.TickCount64;
        public IDisposable After(int delayMs, Action action) => DispatcherTimer.RunOnce(action, TimeSpan.FromMilliseconds(delayMs));
    }

    // Unsupported primitives are filtered before dispatch and acknowledged as unknown.
    private sealed class RainSink(BackRoomRainBridge rain, BackRoomFlashBridge flash) : IBackRoomFxSink
    {
        public void GifRain(int count, int durationMs, double opacity) => rain.GifRain(count, durationMs, opacity);
        public void StopAll() { rain.StopAll(); flash.StopAll(); }
        public void FlashBurst(int amount, double opacity, int gapMs) { }
        public void GlitchWash(int durationMs, double opacity) => flash.GlitchWash(durationMs, opacity);
        public void Subliminal(string text, double opacity) { }
        public void BrainDrain(int durationMs, double level, bool melt) { }
        public void GifFull(BackRoomGif gif, int durationMs, double opacity, Action shown) => flash.GifFull(gif, durationMs, opacity, shown);
        public void Wash(FxRgb color, double peak, BackRoomGif? picture, Action shown) { }
        public bool GifFrom(BackRoomGif gif, FxCssRect? from, int durationMs, double scale, double dim, Action shown) => false;
        public void SpiralLoom(string gifPath, int durationMs, double alpha, bool hold, bool slow) { }
        public void ReleaseSpiralLoom() { }
        public void ReleaseBrainDrain() { }
        public void Tunnel(double level) { }
        public void CancelTunnel() { }
    }
}
