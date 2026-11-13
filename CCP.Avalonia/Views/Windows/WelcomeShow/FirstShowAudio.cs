using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using ConditioningControlPanel.Services.EmiDesk;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.FirstShow;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows.WelcomeShow;

/// <summary>PORTED from ConditioningControlPanel/Services/FirstShow/FirstShowAudio.cs: the recorded cue named by
/// <see cref="FirstShowScript.CueAsset"/> at <see cref="FirstShowScript.CueScale"/> of master (:30-40); master 0 is silence;
/// a new cue stops the last. Each landed speech card gets WPF's deterministic EMI bleepese burst (Speak :19, EmiVox;
/// chirps, never words). Cues resolve through CoreModArt with ContentLocator fallback; suppression matches WPF :18.</summary>
internal sealed class FirstShowAudio : IDisposable
{
    private readonly bool _preview;
    private Func<string, float, string, Action>? _previewPlay;
    internal FirstShowAudio(bool preview = false) => _preview = preview;
    internal Func<Func<string, float, string, Action>> CreatePreviewPlayer = () =>
    {
        var audio = new LibVlcAudio();
        return (path, volume, tag) => audio.PlayOneShot(path, volume, tag, null, null);
    };
    private Action PlayAudio(string path, float volume, string tag) =>
        (_preview && CoreAudio.PlayStoppableProvider == null && CoreAudio.PlayOneShotProvider == null
            ? _previewPlay ??= CreatePreviewPlayer() : Play)(path, volume, tag);
    private Action? _stopCue, _stopVoice;
    private readonly string _cache = Path.Combine(Path.GetTempPath(), "ccp-first-show-" + Guid.NewGuid().ToString("N"));
    private bool _disposed;
    /// <summary>Test seam: (path, volume, tag) -> stop.</summary>
    internal Func<string, float, string, Action> Play = (path, volume, tag) => CoreAudio.PlayStoppable(path, volume, tag);
    internal Func<int>? Master;
    private double Volume => Math.Clamp((Master?.Invoke() ?? CoreSettings.Service?.Current.MasterVolume ?? (_preview ? 65 : CoreSettings.Current.MasterVolume)) / 100.0, 0, 1);
    public void Speak(string text, string mood = "idle")
    {
        if (_disposed || CoreAudio.IsOutputSuppressed || Volume <= 0) return;
        try
        {
            Directory.CreateDirectory(_cache);
            var path = Path.Combine(_cache, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text + mood))) + ".wav");
            if (!File.Exists(path)) File.WriteAllBytes(path, EmiVox.WriteWav(EmiVox.RenderBurst(EmiVox.MakeScore(text, mood))));
            Stop(ref _stopVoice);
            _stopVoice = PlayAudio(path, (float)Volume, "first-show-voice");
        }
        catch (Exception ex) { Log.Debug(ex, "First show speech unavailable"); }
    }
    public void Cue(string name)
    {
        if (_disposed || CoreAudio.IsOutputSuppressed || Volume <= 0) return;
        try
        {
            var rel = FirstShowScript.CueAsset(name).Replace('/', Path.DirectorySeparatorChar);
            var path = CoreModArt.AudioOverridePath(rel) ?? ContentLocator.Resolve(Path.Combine("Resources", "sounds", rel));
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            Stop(ref _stopCue);
            _stopCue = PlayAudio(path, (float)(Volume * FirstShowScript.CueScale), "first-show-cue");
        }
        catch (Exception ex) { Log.Debug(ex, "First show cue unavailable"); }
    }
    private static void Stop(ref Action? handle)
    {
        var stop = handle; handle = null;
        try { stop?.Invoke(); } catch (Exception ex) { Log.Debug(ex, "First show cue stop failed"); }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; Stop(ref _stopCue); Stop(ref _stopVoice);
        try { if (Directory.Exists(_cache)) Directory.Delete(_cache, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
