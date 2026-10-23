using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using ConditioningControlPanel.Services.EmiDesk;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.FirstShow;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows.WelcomeShow;

/// <summary>Disposable show cues and one cached deterministic EMI clip per landed line (WPF FirstShowAudio).</summary>
internal sealed class FirstShowAudio : IDisposable
{
    private Action? _stopCue, _stopVoice;
    internal string CacheDirectory { get; } = Path.Combine(Path.GetTempPath(), "ccp-first-show-" + Guid.NewGuid().ToString("N"));
    private bool _disposed;
    /// <summary>Test seam: (path, volume, tag) -> stop.</summary>
    internal Func<string, float, string, Action> Play = (path, volume, tag) => CoreAudio.PlayStoppable(path, volume, tag);
    private static double Volume => CoreAudio.IsOutputSuppressed ? 0 : Math.Clamp((CoreSettings.Current?.MasterVolume ?? 0) / 100.0, 0, 1);
    public void Speak(string text, string mood = "idle")
    {
        if (_disposed || Volume <= 0 || string.IsNullOrWhiteSpace(text)) return;
        try
        {
            Directory.CreateDirectory(CacheDirectory);
            var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text + mood)));
            var path = Path.Combine(CacheDirectory, key + ".wav");
            if (!File.Exists(path)) File.WriteAllBytes(path, EmiVoxSynthesis.WriteWav(EmiVoxSynthesis.RenderBurst(EmiVoxSynthesis.MakeScore(text, mood))));
            Stop(ref _stopVoice);
            _stopVoice = Play(path, (float)Volume, "first-show-voice");
        }
        catch (Exception ex) { Log.Debug(ex, "First show speech unavailable"); }
    }
    public void Cue(string name)
    {
        if (_disposed || Volume <= 0) return;
        try
        {
            var rel = FirstShowScript.CueAsset(name).Replace('/', Path.DirectorySeparatorChar);
            var path = ContentLocator.Resolve(Path.Combine("Resources", "sounds", rel));
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            Stop(ref _stopCue);
            _stopCue = Play(path, (float)(Volume * FirstShowScript.CueScale), "first-show-cue");
        }
        catch (Exception ex) { Log.Debug(ex, "First show cue unavailable"); }
    }
    private static void Stop(ref Action? slot)
    {
        var stop = slot; slot = null;
        try { stop?.Invoke(); } catch (Exception ex) { Log.Debug(ex, "First show cue stop failed"); }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; Stop(ref _stopVoice); Stop(ref _stopCue);
        try { if (Directory.Exists(CacheDirectory)) Directory.Delete(CacheDirectory, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
