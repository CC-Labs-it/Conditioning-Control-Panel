using System;
using System.IO;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.FirstShow;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows.WelcomeShow;

/// <summary>PORTED from ConditioningControlPanel/Services/FirstShow/FirstShowAudio.cs: the recorded cue named by
/// <see cref="FirstShowScript.CueAsset"/> at <see cref="FirstShowScript.CueScale"/> of master (:30-40); master 0 is silence;
/// a new cue stops the last. ponytail: WPF also spoke each line in EMI bleepese (Speak :19, EmiVox, still WPF-only), and
/// resolved cues through ModResourceResolver; here lines are silent and cues come from ContentLocator (as EmiSfx).</summary>
internal sealed class FirstShowAudio : IDisposable
{
    private Action? _stopCue;
    private bool _disposed;
    /// <summary>Test seam: (path, volume, tag) -> stop.</summary>
    internal Func<string, float, string, Action> Play = (path, volume, tag) => CoreAudio.PlayStoppable(path, volume, tag);
    private static double Volume => Math.Clamp((CoreSettings.Current?.MasterVolume ?? 0) / 100.0, 0, 1);
    public void Cue(string name)
    {
        if (_disposed || Volume <= 0) return;
        try
        {
            var rel = FirstShowScript.CueAsset(name).Replace('/', Path.DirectorySeparatorChar);
            var path = ContentLocator.Resolve(Path.Combine("Resources", "sounds", rel));
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            Stop();
            _stopCue = Play(path, (float)(Volume * FirstShowScript.CueScale), "first-show-cue");
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
