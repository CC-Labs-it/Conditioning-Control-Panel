using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.FirstShow;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows.WelcomeShow;

/// <summary>WPF Services/FirstShow/FirstShowAudio.cs:19-47: cached EMI synthesis and independent cues.</summary>
internal sealed class FirstShowAudio : IDisposable
{
    private readonly object _gate = new();
    private int _pending;
    private Action? _stopCue, _stopVoice;
    internal string CacheDirectory { get; } = Path.Combine(Path.GetTempPath(), "ccp-first-show-" + Guid.NewGuid().ToString("N"));
    public void Speak(string text, string mood = "idle")
    {
        lock (_gate)
        {
            if (_disposed || CoreAudio.IsOutputSuppressed || Volume <= 0) return;
            try
            {
                Directory.CreateDirectory(CacheDirectory);
                var path = Path.Combine(CacheDirectory, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text + mood))) + ".wav");
                if (!File.Exists(path)) File.WriteAllBytes(path, FirstShowVoice.WriteWav(FirstShowVoice.RenderBurst(FirstShowVoice.MakeScore(text, mood))));
                Stop(ref _stopVoice);
                _stopVoice = PlayTracked(path, (float)Volume, "first-show-voice");
            }
            catch (Exception ex) { Log.Debug(ex, "First show speech unavailable"); }
        }
    }
    private bool _disposed;
    /// <summary>Test seam: completion must fire after playback resources are released, including on stop.</summary>
    internal Func<string, float, string, Action, Action> Play =
        (path, volume, tag, finished) => CoreAudio.PlayStoppable(path, volume, tag, onFinished: finished);
    private Action PlayTracked(string path, float volume, string tag)
    {
        _pending++;
        int completed = 0;
        void Finished()
        {
            if (Interlocked.Exchange(ref completed, 1) != 0) return;
            lock (_gate) { _pending--; DeleteCacheWhenReleased(); }
        }
        try { return Play(path, volume, tag, Finished); }
        catch { Finished(); throw; }
    }
    private static double Volume => Math.Clamp((CoreSettings.Current?.MasterVolume ?? 0) / 100.0, 0, 1);
    public void Cue(string name)
    {
        lock (_gate)
        {
            if (_disposed || CoreAudio.IsOutputSuppressed || Volume <= 0) return;
            try
            {
                var rel = FirstShowScript.CueAsset(name).Replace('/', Path.DirectorySeparatorChar);
                var path = CoreModArt.AudioOverridePath(rel) ?? ContentLocator.Resolve(Path.Combine("Resources", "sounds", rel));
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
                Stop(ref _stopCue);
                _stopCue = PlayTracked(path, (float)(Volume * FirstShowScript.CueScale), "first-show-cue");
            }
            catch (Exception ex) { Log.Debug(ex, "First show cue unavailable"); }
        }
    }
    private static void Stop(ref Action? channel)
    {
        var stop = channel; channel = null;
        try { stop?.Invoke(); } catch (Exception ex) { Log.Debug(ex, "First show cue stop failed"); }
    }
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true; Stop(ref _stopVoice); Stop(ref _stopCue);
            DeleteCacheWhenReleased();
        }
    }
    private void DeleteCacheWhenReleased()
    {
        // WPF FirstShowAudio.cs:46-47 closes both players before deleting. LibVLC releases
        // asynchronously; count replaced players too, until their completion callbacks arrive.
        if (!_disposed || _pending != 0) return;
        try { if (Directory.Exists(CacheDirectory)) Directory.Delete(CacheDirectory, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
