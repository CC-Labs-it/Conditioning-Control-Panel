using System;
using System.IO;
using System.Linq;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;

namespace ConditioningControlPanel.Avalonia.Views.AvatarTube;

public partial class AvatarTubeWindow
{
    private AvatarPortraitLoader? _portrait, _paintedPortrait;
    private Bitmap? _portraitImage, _portraitOld;
    private Image? _portraitActive;
    private Action _cancelPortraitStep = static () => { }, _cancelPortraitFade = static () => { };
    private int _portraitStepVersion, _portraitFadeVersion, _portraitStep, _portraitPose;
    private int[] _portraitOrder = Array.Empty<int>(); private string? _portraitEmotion;
    private double _portraitStepMs, _portraitLastMs;
    private readonly System.Collections.Generic.Dictionary<string, double> _portraitDurations = new();
    internal string? PortraitPath { get; private set; }
    internal bool IsPortraitMode => _portraitActive != null;
    internal bool IsPoseRotating => _poseTimer.IsEnabled;
    internal Func<TimeSpan, Action, Action> PortraitStepScheduler = SchedulePortrait;
    internal Func<TimeSpan, Action, Action> PortraitFadeScheduler = SchedulePortrait;
    private static Action SchedulePortrait(TimeSpan delay, Action fire)
    {
        var timer = new DispatcherTimer { Interval = delay };
        timer.Tick += (_, _) => { timer.Stop(); fire(); };
        timer.Start();
        return timer.Stop;
    }

    // WPF Avatar.cs:1018-1119: enter on mod change, hold idle still, restore legacy on exit.
    private void LoadPortrait() => _portrait = SingleEmoteSet() == null ? AvatarPortraitLoader.Load() : null;

    private bool PaintPortrait()
    {
        if (_portrait == null || ResolveV1EmoteFolder() != null) return false;
        if (ReferenceEquals(_paintedPortrait, _portrait)) {
            var bucket = _portrait.Bucket(_currentAvatarSet - 1, _portraitEmotion ?? _portrait.Manifest.IdleEmotion);
            if (bucket.Length > 0) { if (_portraitPose >= bucket.Length) _portraitPose = 0; FadePortrait(bucket[_portraitPose]); }
            UpdatePortraitTitle(); return true;
        }
        string? path = null;
        Bitmap? image = null;
        foreach (var candidate in _portrait.Bucket(_currentAvatarSet - 1, _portrait.Manifest.IdleEmotion))
            if ((image = _portrait.Open(candidate)) != null) { path = candidate; break; }
        StopRainPlayback();
        _poseTimer.Stop();
        var previous = image == null ? _portraitActive?.Source ?? _imgAvatar.Source : null;
        var retained = ReferenceEquals(previous, _portraitImage) ? _portraitImage : null;
        if (retained != null) _portraitImage = null;
        ClearPortraitImage();
        _paintedPortrait = _portrait; _portraitImage = image ?? retained;
        _portraitActive = _imgAvatar; _imgAvatar.Source = image ?? previous;
        PortraitPath = path;
        var second = this.FindControl<Image>("ImgAvatarB")!;
        second.IsVisible = true;
        _imgAvatar.Classes.Set("portrait-mode", true); second.Classes.Set("portrait-mode", true);
        this.FindControl<Control>("MistOverlay")!.IsVisible = true;
        (_imgAvatar.MaxHeight, second.MaxHeight, _imgAvatar.MaxWidth, second.MaxWidth) = (306 * 0.88, 306 * 0.88, 198 * 0.88, 198 * 0.88);
        _avatarBorder.RenderTransform = new TranslateTransform(10, -30);
        UpdatePortraitTitle();
        return true;
    }

    private void UpdatePortraitTitle()
    {
        var skin = _portrait!.Manifest.Skins[Math.Clamp(_currentAvatarSet - 1, 0, _portrait.Manifest.Skins.Count - 1)];
        _txtAvatarTitle.Text = string.IsNullOrWhiteSpace(skin.Title) ? skin.Id : skin.Title;
        _txtAvatarLevel.IsVisible = false;
    }

    // WPF Avatar.cs:1217-1384: mapped line, else mood or affirmation; 2-5 poses, then still idle.
    private void PlayPortraitLine(string? lineId, string? audioPath, string text, string? mood, bool heldIdle)
    {
        if (!IsPortraitMode || _portrait == null) return;
        var emotion = _portrait.EmotionForLine(lineId);
        if (string.IsNullOrEmpty(emotion))
        {
            string[] affirmations = { "alluring", "alluring", "alluring", "entrancing", "dreamy", "teasing" };
            emotion = !string.IsNullOrWhiteSpace(mood) ? AvatarPortraitLoader.EmotionForMood(mood) : affirmations[_random.Next(affirmations.Length)];
        }
        var bucket = _portrait.Bucket(_currentAvatarSet - 1, emotion!);
        if (bucket.Length == 0) return;
        double seconds = heldIdle ? 0 : audioPath != null ? PortraitAudioDuration(audioPath) : string.IsNullOrWhiteSpace(text) ? 2.5
            : Math.Clamp(0.45 * text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length + 0.8, 2.0, 7.0);
        int count = seconds <= 0 ? 2 : Math.Clamp((int)Math.Round(seconds) - 1, 2, 5);
        var order = new int[count];
        var pool = new System.Collections.Generic.List<int>(); int last = -1;
        for (int i = 0; i < count; i++)
        {
            if (pool.Count == 0) { pool.AddRange(Enumerable.Range(0, bucket.Length).OrderBy(_ => _random.Next())); if (pool.Count > 1 && pool[0] == last) (pool[0], pool[1]) = (pool[1], pool[0]); }
            order[i] = last = pool[0]; pool.RemoveAt(0);
        }
        CancelPortraitStep();
        _portraitEmotion = emotion; _portraitOrder = order; _portraitStep = 0;
        (_portraitStepMs, _portraitLastMs) = seconds > 0 && seconds < 3.5 ? (500, 1000) : (1000, 2000);
        FadePortrait(bucket[_portraitPose = order[0]]);
        SchedulePortraitStep(_portraitStepMs);
    }

    // NAudio's portable frame reader avoids the Windows-only AudioFileReader used by WPF.
    private double PortraitAudioDuration(string path)
    {
        if (_portraitDurations.TryGetValue(path, out var duration)) return duration;
        try
        {
            if (Path.GetExtension(path).Equals(".wav", StringComparison.OrdinalIgnoreCase))
            { using var wave = new NAudio.Wave.WaveFileReader(path); duration = wave.TotalTime.TotalSeconds; }
            else if (Path.GetExtension(path).Equals(".mp3", StringComparison.OrdinalIgnoreCase))
            {
                using var stream = File.OpenRead(path); NAudio.Wave.Mp3Frame? frame;
                while ((frame = NAudio.Wave.Mp3Frame.LoadFromStream(stream)) != null)
                    if (frame.SampleRate > 0) duration += (double)frame.SampleCount / frame.SampleRate;
            }
        }
        catch (Exception ex) { Serilog.Log.Debug(ex, "Portrait audio duration unavailable"); duration = 0; }
        return _portraitDurations[path] = duration;
    }

    private void SchedulePortraitStep(double ms)
    {
        int version = _portraitStepVersion;
        _cancelPortraitStep = PortraitStepScheduler(TimeSpan.FromMilliseconds(ms), () =>
        {
            if (version != _portraitStepVersion || _portrait == null || !IsPortraitMode) return;
            if (++_portraitStep >= _portraitOrder.Length)
            {
                _portraitEmotion = null; var idle = _portrait.Bucket(_currentAvatarSet - 1, _portrait.Manifest.IdleEmotion);
                if (idle.Length > 0) FadePortrait(idle[_portraitPose = _random.Next(idle.Length)]);
                return;
            }
            var bucket = _portrait.Bucket(_currentAvatarSet - 1, _portraitEmotion!);
            if (bucket.Length == 0) { _portraitEmotion = null; return; }
            FadePortrait(bucket[_portraitPose = _portraitOrder[_portraitStep] % bucket.Length]);
            SchedulePortraitStep(_portraitStep == _portraitOrder.Length - 1 ? _portraitLastMs : _portraitStepMs);
        });
    }

    private void CancelPortraitStep() { ++_portraitStepVersion; _cancelPortraitStep(); _cancelPortraitStep = static () => { }; }

    // WPF Avatar.cs:1169-1211: preempt a fade, then crossfade both images.
    private void FadePortrait(string path)
    {
        if (_portrait == null || _portraitActive == null || path == PortraitPath) return;
        var image = _portrait.Open(path);
        if (image == null) return;
        FinishPortraitFade();
        var previous = _portraitActive;
        var next = previous == _imgAvatar ? this.FindControl<Image>("ImgAvatarB")! : _imgAvatar;
        next.Source = image;
        next.IsVisible = true;
        next.Opacity = 0;
        var duration = TimeSpan.FromMilliseconds(Math.Max(60, (_portrait.Manifest.Director?.CrossfadeFrames ?? 4) * 38));
        previous.Transitions = new Transitions { new DoubleTransition { Property = OpacityProperty, Duration = duration } };
        next.Transitions = new Transitions { new DoubleTransition { Property = OpacityProperty, Duration = duration } };
        _portraitOld = _portraitImage;
        _portraitImage = image;
        _portraitActive = next;
        PortraitPath = path;
        previous.Opacity = 0;
        next.Opacity = 1;
        int version = _portraitFadeVersion;
        _cancelPortraitFade = PortraitFadeScheduler(duration, () => { if (version == _portraitFadeVersion) FinishPortraitFade(); });
    }

    private void FinishPortraitFade()
    {
        ++_portraitFadeVersion;
        _cancelPortraitFade(); _cancelPortraitFade = static () => { };
        if (_portraitActive != null)
        {
            var hidden = _portraitActive == _imgAvatar ? this.FindControl<Image>("ImgAvatarB")! : _imgAvatar;
            hidden.Transitions = null; hidden.Opacity = 0; hidden.Source = null;
            _portraitActive.Transitions = null; _portraitActive.Opacity = 1;
        }
        _portraitOld?.Dispose(); _portraitOld = null;
    }

    private void ClearPortraitImage()
    {
        CancelPortraitStep();
        FinishPortraitFade();
        var second = this.FindControl<Image>("ImgAvatarB")!;
        this.FindControl<Control>("MistOverlay")!.IsVisible = false;
        _imgAvatar.Classes.Set("portrait-mode", false); second.Classes.Set("portrait-mode", false);
        _imgAvatar.Source = null; _imgAvatar.Opacity = 1;
        second.Source = null; second.Opacity = 0; second.IsVisible = false;
        _portraitActive = null; _paintedPortrait = null; _portraitEmotion = null; _portraitPose = 0;
        _portraitImage?.Dispose(); _portraitImage = null; PortraitPath = null;
        (_imgAvatar.MaxHeight, second.MaxHeight, _imgAvatar.MaxWidth, second.MaxWidth) = (306, 306, 198, 198);
    }
}
