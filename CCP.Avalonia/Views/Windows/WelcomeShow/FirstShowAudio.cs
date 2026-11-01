using System;
using System.IO;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.FirstShow;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows.WelcomeShow;

/// <summary>PORTED from ConditioningControlPanel/Services/FirstShow/FirstShowAudio.cs: the recorded cue named by
/// <see cref="FirstShowScript.CueAsset"/> at <see cref="FirstShowScript.CueScale"/> of master (:30-40); master 0 is silence;
/// a new cue stops the last. ponytail: WPF also spoke each line in EMI bleepese (Speak :19, EmiVox, still WPF-only), and
/// resolved cues through ModResourceResolver (:37); this head uses CoreModArt with ContentLocator fallback.
/// Suppression matches WPF :18. Lines remain silent.</summary>
internal sealed class FirstShowAudio : IDisposable
{
    private readonly bool _preview;
    private Action? _stopCue;
    private Func<string, float, string, Action>? _previewPlay;
    private bool _disposed;
    internal FirstShowAudio(bool preview = false) { _preview = preview; }
    // Preview bypasses normal startup (WPF App.xaml.cs:1996-2003), but WPF's MediaPlayer is independent of App.Audio.
    // Create a local player only if needed; do not seed global audio or the ducking service.
    internal Func<Func<string, float, string, Action>> CreatePreviewPlayer = () =>
    {
        var audio = new LibVlcAudio();
        return (path, volume, tag) => audio.PlayOneShot(path, volume, tag, null, null);
    };
    /// <summary>Test seam: (path, volume, tag) -> stop.</summary>
    internal Func<string, float, string, Action> Play = (path, volume, tag) => CoreAudio.PlayStoppable(path, volume, tag);
    // WPF FirstShowAudio.cs:18: an isolated preview uses 65 only when settings are absent.
    private double Volume => Math.Clamp((CoreSettings.Service?.Current.MasterVolume ?? (_preview ? 65 : 0)) / 100.0, 0, 1);
    public void Cue(string name)
    {
        if (_disposed || CoreAudio.IsOutputSuppressed || Volume <= 0) return;
        try
        {
            var rel = FirstShowScript.CueAsset(name).Replace('/', Path.DirectorySeparatorChar);
            var path = CoreModArt.AudioOverridePath(rel) ?? ContentLocator.Resolve(Path.Combine("Resources", "sounds", rel));
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            Stop();
            var play = _preview && CoreAudio.PlayStoppableProvider == null && CoreAudio.PlayOneShotProvider == null
                ? _previewPlay ??= CreatePreviewPlayer() : Play;
            _stopCue = play(path, (float)(Volume * FirstShowScript.CueScale), "first-show-cue");
        }
        catch (Exception ex) { Log.Debug(ex, "First show cue unavailable"); }
    }
    private void Stop()
    {
        var stop = _stopCue; _stopCue = null;
        try { stop?.Invoke(); } catch (Exception ex) { Log.Debug(ex, "First show cue stop failed"); }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; Stop();
    }
}
