using System;
using System.Diagnostics;
using Avalonia.Threading;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>WPF ConditioningControlPanel/Services/MantraChantService.cs:144-159,333-335,337-355:
/// transport on the existing LibVLC voice player.</summary>
internal sealed class MantraChantHost : IDisposable
{
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly DispatcherTimer _watch;
    private readonly MantraChantLoop _loop;
    private MindWipePlayer.IVoice? _voice;
    internal MantraChantHost()
    {
        _loop = new(() => App.MantraVoice.HasVoicedMantras(), () =>
        {
            var entry = App.MantraVoice.NextMantra();
            return MantraChantLoop.ResolveClips(App.MantraVoice.ResolveAudio(entry?.PromptAudio),
                App.MantraVoice.ResolveAudio(entry?.ResponseAudio));
        }, () => (Views.AvatarTube.AvatarTubeWindow.Live?.IsMuted ?? CoreSettings.Current.AvatarMuted), () => CoreSettings.Current.MantraChantGapSeconds,
        (path, generation) =>
        {
            StopAudio();
            _voice = LibVlcAudio.Instance?.PlayVoice(path, Volume, false,
                () => Dispatcher.UIThread.Post(() => { _loop!.ClipEnded(generation, _clock.Elapsed); ScheduleWatch(); }));
            return _voice != null;
        }, StopAudio);
        _watch = new DispatcherTimer { Interval = TimeSpan.FromSeconds(.5) };
        _watch.Tick += (_, _) => { _loop.Tick(_clock.Elapsed); ApplyVolume(); ScheduleWatch(); };
    }
    private double Volume => MantraChantLoop.ResolveVolume((Views.AvatarTube.AvatarTubeWindow.Live?.IsMuted ?? CoreSettings.Current.AvatarMuted),
        CoreSettings.Current.MantraChantVolume, CoreSettings.Current.MasterVolume);
    internal bool IsRunning => _loop.IsRunning;
    internal void Start() { _loop.Start(_clock.Elapsed); ScheduleWatch(); }
    private void ScheduleWatch()
    {
        _watch.Stop();
        if (!_loop.IsRunning) return;
        // Keep WPF's 500 ms mute watch, but wake at the exact pair/gap deadline too.
        var remaining = _loop.NextDue is { } due ? (due - _clock.Elapsed).TotalMilliseconds : 500;
        _watch.Interval = TimeSpan.FromMilliseconds(Math.Clamp(remaining, 1, 500));
        _watch.Start();
    }
    internal void Stop()
    {
        _loop.Stop();
        if (Dispatcher.UIThread.CheckAccess()) _watch.Stop();
        else Dispatcher.UIThread.Post(() => { if (!_loop.IsRunning) _watch.Stop(); });
    }
    internal static void StopForPanic() => App.MantraChant.StopAndDisarm();
    internal void StopAndDisarm()
    {
        Stop();
        if (!CoreSettings.Current.MantraChantEnabled) return;
        CoreSettings.Current.MantraChantEnabled = false;
        CoreSettings.Save();
    }
    internal void ApplyVolume() { if (_voice != null) _voice.Volume = Volume; }
    private void StopAudio() { var voice = System.Threading.Interlocked.Exchange(ref _voice, null); voice?.Dispose(); }
    public void Dispose() => Stop();
}
