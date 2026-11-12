using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Deeper;
using ConditioningControlPanel.Models.Deeper;
using ConditioningControlPanel.Services.Deeper;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    // WPF RealActionDispatcher.cs:46. Effects without an equivalent head service remain unported.
    internal sealed class BrowserEnhancementDispatcher : IActionDispatcher
    {
        public async Task DispatchAsync(EnhancementAction action, EnhancementDispatchContext ctx, CancellationToken ct = default)
        {
            if (ct.IsCancellationRequested) return;
            try
            {
                switch (action)
                {
                    case SeekAction seek:
                        var band = Resolve(ctx, seek.RegionId);
                        var target = seek.Target switch { SeekTargets.Time => seek.Time, SeekTargets.RegionStart => band?.start, SeekTargets.RegionEnd => band?.end, _ => null };
                        if (target.HasValue) ctx.Source.Seek(target.Value);
                        break;
                    case LoopRegionAction loop:
                        var region = Resolve(ctx, loop.RegionId ?? ctx.CurrentRegionId);
                        if (region.HasValue) ctx.Source.Seek(region.Value.start);
                        break;
                    case PauseAction: ctx.Source.Pause(); break;
                    case PlayAudioAction audio:
                        var path = Path.IsPathRooted(audio.Path) ? audio.Path : Path.Combine(CorePaths.EffectiveAssets, audio.Path);
                        if (File.Exists(path)) CoreAudio.PlayOneShot(path, Math.Clamp(audio.Volume, 0, 100) / 100f, "deeper");
                        break;
                    case TriggerHapticAction haptic: await Haptic(haptic); break;
                    case TriggerEffectAction effect when effect.EffectType == EffectTypes.Haptic:
                        await Haptic(new TriggerHapticAction { PatternName = effect.PatternName, CustomPattern = effect.CustomPattern,
                            Intensity = effect.Intensity, DurationMs = effect.DurationMs, Phase = effect.Phase, EffectId = effect.EffectId });
                        break;
                    case ScreenShakeAction shake: ScreenShake.Shake(shake.Intensity, shake.DurationMs); break;
                }
            }
            catch (Exception ex) { Log.Debug("Deeper action failed: {Error}", ex.Message); }
        }
        private static (double start, double end)? Resolve(EnhancementDispatchContext ctx, string? id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            var item = ctx.Enhancement.TimelineItems.FirstOrDefault(i => i.Kind == TimelineItemKind.Rule && i.Id == id && i.Duration > 0 && i.Duration < double.MaxValue);
            if (item != null) return (item.Start, item.Start + item.Duration);
            var region = ctx.Enhancement.Regions.FirstOrDefault(r => r.Id == id);
            return region == null ? null : (region.Start, region.End);
        }
        private static async Task Haptic(TriggerHapticAction haptic)
        {
            var service = CoreHaptics.Service;
            if (service == null) return;
            if (haptic.Phase == EffectPhase.Stop) { await service.StopAsync(); return; }
            IList<double[]>? pattern = haptic.CustomPattern;
            if ((pattern == null || pattern.Count == 0) && !string.IsNullOrEmpty(haptic.PatternName)
                && StockHapticPatterns.TryGet(haptic.PatternName, out var named)) pattern = named;
            if (pattern == null || pattern.Count == 0 || !service.Settings.Enabled || !service.IsConnected) return;
            var samples = StockHapticPatterns.Sample(pattern, haptic.Intensity, haptic.DurationMs);
            if (samples.Length == 0) return;
            if (haptic.Phase == EffectPhase.Restart) await service.StopAsync();
            await service.SetSyncPatternAsync(samples, haptic.DurationMs, haptic.Target);
        }
    }

    // WPF WebcamEnhancementEvents.cs:15. The tracker publishes these events on the UI thread.
    internal sealed class BrowserEnhancementWebcam : IEnhancementWebcam, IDisposable
    {
        private readonly WebcamTracker _tracker = WebcamTracker.Instance;
        public event Action? OnBlink;
        public event Action? OnMouthOpen;
        public event Action<double, double>? OnGazeMove;
        public event Action? OnFaceLost;
        public event Action? OnFaceFound;
        public BrowserEnhancementWebcam()
        {
            _tracker.OnBlink += Blink; _tracker.OnMouthOpen += Mouth; _tracker.OnGazeMove += Gaze;
            _tracker.OnFaceLost += Lost; _tracker.OnFaceFound += Found;
        }
        private void Blink() => OnBlink?.Invoke();
        private void Mouth() => OnMouthOpen?.Invoke();
        private void Gaze(Point p) => OnGazeMove?.Invoke(p.X, p.Y);
        private void Lost() => OnFaceLost?.Invoke();
        private void Found() => OnFaceFound?.Invoke();
        public void Dispose()
        {
            _tracker.OnBlink -= Blink; _tracker.OnMouthOpen -= Mouth; _tracker.OnGazeMove -= Gaze;
            _tracker.OnFaceLost -= Lost; _tracker.OnFaceFound -= Found;
        }
    }
}
