using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using global::Avalonia.Controls.ApplicationLifetimes;
using global::Avalonia.Threading;
using ConditioningControlPanel.Models.Deeper;
using ConditioningControlPanel.Services.Deeper;

namespace ConditioningControlPanel.Avalonia.Platform.Deeper;

// WPF RealActionDispatcher: the head supplies the actual effects and playback commands.
internal sealed class BrowserEnhancementDispatcher : IActionDispatcher, IEnhancementRunCleanup
{
    private readonly object _audioGate = new();
    private readonly HashSet<Action> _audioStops = new();
    private volatile bool _retired;

    // EnhancementEngine.Stop calls this on unbind and panic.
    public void ResetOverlayBands()
    {
        Action[] stops;
        lock (_audioGate) { _retired = true; stops = _audioStops.ToArray(); _audioStops.Clear(); }
        foreach (var stop in stops) { try { stop(); } catch { } }
    }
    public void StopOneShotFlashes() { }
    public void StopOneShotSubliminals() { }

    private void PlayAudio(string path, float volume, bool duckOtherAudio)
    {
        lock (_audioGate)
        {
            if (_retired || CoreAudio.PlayStoppableProvider == null) return;
            foreach (var previous in _audioStops.ToArray()) { try { previous(); } catch { } } // WPF AudioService.cs:833 StopSound first
            var ducked = duckOtherAudio && CoreSettings.Current.AudioDuckingEnabled;
            if (ducked) CoreAudio.Duck(CoreSettings.Current.DuckingLevel);
            var generation = CoreAudio.DuckGeneration;
            Action? stop = null;
            bool finished = false;
            void Finish()
            {
                lock (_audioGate)
                {
                    if (finished) return;
                    finished = true;
                    if (stop != null) _audioStops.Remove(stop);
                    if (ducked) CoreAudio.Unduck(generation);
                }
            }
            var stopPlayer = CoreAudio.PlayStoppable(path, volume, "deeper", onFinished: Finish);
            stop = () => { try { stopPlayer(); } finally { Finish(); } };
            if (!finished) _audioStops.Add(stop);
        }
    }

    // Do not claim a match is active when this head cannot run its effects.
    internal static bool Supports(Enhancement enhancement)
    {
        static bool EffectSupported(string? type) => type is EffectTypes.Haptic or EffectTypes.Flash or EffectTypes.Subliminal or EffectTypes.Bubble;
        static bool ActionSupported(EnhancementAction? action) => action is not TriggerEffectAction effect || EffectSupported(effect.EffectType);
        return enhancement.Rules.All(r => !r.Enabled || ActionSupported(r.Action))
            && enhancement.TimelineItems.All(i => !i.Enabled || (i.Kind == TimelineItemKind.Effect ? EffectSupported(i.EffectType) : ActionSupported(i.Action)));
    }
    public async Task DispatchAsync(EnhancementAction action, EnhancementDispatchContext ctx, CancellationToken ct = default)
    {
        if (ct.IsCancellationRequested) return;
        switch (action)
        {
            case PauseAction: ctx.Source.Pause(); break;
            case SeekAction seek:
                var region = Resolve(ctx, seek.RegionId);
                var target = seek.Target switch { SeekTargets.Time => seek.Time, SeekTargets.RegionStart => region?.start, SeekTargets.RegionEnd => region?.end, _ => null };
                if (target.HasValue) ctx.Source.Seek(target.Value);
                break;
            case LoopRegionAction loop:
                var band = Resolve(ctx, loop.RegionId ?? ctx.CurrentRegionId);
                if (band.HasValue) ctx.Source.Seek(band.Value.start);
                break;
            case PlayAudioAction audio when !string.IsNullOrEmpty(audio.Path):
                var path = Path.IsPathRooted(audio.Path) ? audio.Path : Path.Combine(CorePaths.EffectiveAssets, audio.Path);
                if (File.Exists(path)) PlayAudio(path, CoreAudio.SoundGain(audio.Volume, CoreSettings.Current.MasterVolume), audio.DuckOtherAudio);
                break;
            case ScreenShakeAction shake: Views.Deeper.ScreenShake.Shake(shake.Intensity, shake.DurationMs); break;
            case TriggerHapticAction haptic: await Haptic(haptic); break;
            case TriggerEffectAction effect when effect.EffectType == EffectTypes.Haptic:
                await Haptic(new TriggerHapticAction { PatternName = effect.PatternName, CustomPattern = effect.CustomPattern, Intensity = effect.Intensity, DurationMs = effect.DurationMs, Phase = effect.Phase });
                break;
            case TriggerEffectAction effect when effect.EffectType == EffectTypes.Flash && effect.Phase != EffectPhase.Stop:
                // WPF RealActionDispatcher.cs:188-194 -> FlashService.cs:793: one picture for the authored duration.
                Dispatcher.UIThread.Post(() => { if (!ct.IsCancellationRequested && !_retired && Host is { } h) Views.Overlays.FlashOverlay.TriggerOnce(h, amount: 1, durationMs: effect.DurationMs); }); break;
            case TriggerEffectAction effect when effect.EffectType == EffectTypes.Bubble: BubbleBurst(effect.DurationMs, ct); break;
            case TriggerEffectAction effect when effect.EffectType == EffectTypes.Subliminal && effect.Phase != EffectPhase.Stop && !string.IsNullOrWhiteSpace(effect.Text):
                Views.Overlays.SubliminalWhisperShow.Custom(effect.Text, durationMs: effect.DurationMs, suppressHaptic: effect.SuppressHaptic); break;
        }
    }
    private static global::Avalonia.Visual? Host => (global::Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    // WPF RealActionDispatcher.cs:345-395: bursts share one claim on the app-wide field; the last one
    // to end stops it only if a burst started it, and the engine's run token releases it early.
    private static int _bubbleHolds;
    private static bool _bubbleOwns;
    private static void BubbleBurst(int durationMs, CancellationToken ct) => Dispatcher.UIThread.Post(() =>
    {
        if (ct.IsCancellationRequested) return;
        if (_bubbleHolds++ == 0) _bubbleOwns = !Views.Overlays.BubbleOverlay.IsRunning;
        ConditioningControlPanel.Services.CoreBubbles.Start();
        var released = false;
        void Release() { if (released) return; released = true; if (--_bubbleHolds == 0 && _bubbleOwns) { _bubbleOwns = false; ConditioningControlPanel.Services.CoreBubbles.Stop(); } }
        var timer = DispatcherTimer.RunOnce(Release, TimeSpan.FromMilliseconds(Math.Max(50, durationMs)));
        if (ct.CanBeCanceled) ct.Register(() => Dispatcher.UIThread.Post(() => { timer.Dispose(); Release(); }));
    });
    private static (double start, double end)? Resolve(EnhancementDispatchContext ctx, string? id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        var item = ctx.Enhancement.TimelineItems.FirstOrDefault(i => i.Kind == TimelineItemKind.Rule && i.Id == id && i.Duration > 0 && i.Duration < double.MaxValue);
        if (item != null) return (item.Start, item.Start + item.Duration);
        var region = ctx.Enhancement.Regions.FirstOrDefault(r => r.Id == id);
        return region == null ? null : (region.Start, region.End);
    }
    private static async Task Haptic(TriggerHapticAction action)
    {
        var service = CoreHaptics.Service;
        if (service == null) return;
        if (action.Phase == EffectPhase.Stop) { await service.StopAsync(); return; }
        IList<double[]>? frames = action.CustomPattern;
        if ((frames == null || frames.Count == 0) && action.PatternName != null && StockHapticPatterns.TryGet(action.PatternName, out var named)) frames = named;
        if (frames == null || !service.Settings.Enabled || !service.IsConnected) return;
        var samples = StockHapticPatterns.Sample(frames, action.Intensity, action.DurationMs);
        if (samples.Length == 0) return;
        if (action.Phase == EffectPhase.Restart) await service.StopAsync();
        await service.SetSyncPatternAsync(samples, action.DurationMs, action.Target);
    }
}

// WPF webcam events are forwarded only for the lifetime of a bound engine.
internal sealed class BrowserEnhancementWebcam : IEnhancementWebcam, IDisposable
{
    private readonly WebcamTracker _tracker = WebcamTracker.Instance;
    public event Action? OnBlink, OnMouthOpen, OnFaceLost, OnFaceFound;
    public event Action<double, double>? OnGazeMove;
    public BrowserEnhancementWebcam()
    {
        _tracker.OnBlink += Blink; _tracker.OnMouthOpen += Mouth; _tracker.OnFaceLost += Lost;
        _tracker.OnFaceFound += Found; _tracker.OnGazeMove += Gaze;
    }
    private void Blink() => OnBlink?.Invoke();
    private void Mouth() => OnMouthOpen?.Invoke();
    private void Lost() => OnFaceLost?.Invoke();
    private void Found() => OnFaceFound?.Invoke();
    private void Gaze(global::Avalonia.Point p) => OnGazeMove?.Invoke(p.X, p.Y);
    public void Dispose()
    {
        _tracker.OnBlink -= Blink; _tracker.OnMouthOpen -= Mouth; _tracker.OnFaceLost -= Lost;
        _tracker.OnFaceFound -= Found; _tracker.OnGazeMove -= Gaze;
    }
}
