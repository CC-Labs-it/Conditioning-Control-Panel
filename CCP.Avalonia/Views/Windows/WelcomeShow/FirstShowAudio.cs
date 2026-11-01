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
    private Action? _stopCue, _disposeCue;
    private Func<string, float, string, Action>? _cuePlay;
    private bool _disposed;
    internal FirstShowAudio(bool preview = false)
    {
        _preview = preview;
        // WPF preview bypasses service startup, but its MediaPlayer still plays (App.xaml.cs:1996-2003).
        CreateCuePlayer = () =>
        {
            var player = new LibVlcAudio.FirstShowCue();
            _disposeCue = player.Dispose;
            return player.Play;
        };
        // Seed() installs this method group for the normal show. Use the same safe cue player there;
        // other providers keep their own playback path (WPF FirstShowAudio.cs:31-39).
        Play = (path, volume, tag) => CoreAudio.PlayStoppableProvider?.Target is LibVlcAudio
            ? (_cuePlay ??= CreateCuePlayer())(path, volume, tag)
            : CoreAudio.PlayStoppable(path, volume, tag);
    }
    internal Func<Func<string, float, string, Action>> CreateCuePlayer;
    /// <summary>Test seam: (path, volume, tag) -> stop.</summary>
    internal Func<string, float, string, Action> Play;
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
            if (!Stop()) return;
            var play = _preview && CoreAudio.PlayStoppableProvider == null && CoreAudio.PlayOneShotProvider == null
                ? _cuePlay ??= CreateCuePlayer() : Play;
            _stopCue = play(path, (float)(Volume * FirstShowScript.CueScale), "first-show-cue");
        }
        catch (Exception ex) { Log.Debug(ex, "First show cue unavailable"); }
    }
    private bool Stop()
    {
        try { _stopCue?.Invoke(); _stopCue = null; return true; }
        catch (Exception ex) { Log.Debug(ex, "First show cue stop failed"); return false; }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; Stop();
        try { _disposeCue?.Invoke(); } catch (Exception ex) { Log.Debug(ex, "First show cue audio cleanup failed"); }
        _disposeCue = null; _cuePlay = null; _stopCue = null;
    }
}
