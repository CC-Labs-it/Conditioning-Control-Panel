using System;
using System.Collections.Generic;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Safety;
using ConditioningControlPanel.Services.Super;
using ConditioningControlPanel.SuperCore;

namespace ConditioningControlPanel.Avalonia.Views.Overlays;

// WPF LightsDownService.cs:33-48,113-174. The room windows stay behind the video;
// the in-video layer shades its background and draws the frame above the image.
internal static partial class LightsDownOverlay
{
    private static DispatcherTimer? _timer;
    internal static bool VideoTimerRunning => _timer != null;
    private static readonly Stopwatch Clock = new();
    private static readonly List<(Window Window, Panel Parent, VideoLight Light)> Lights = new();
    private static MandatoryVideoOverlay? _video;
    private static LightsDownState? _state;
    private static double _last;
    private static bool _refused;
    private static Point? _gaze;
    private static double _gazeAt;

    internal static void StartVideo(MandatoryVideoOverlay video)
    {
        CloseAll();
        _video = video;
        _refused = false;
        WebcamTracker.Instance.OnGazeMove += OnGaze;
        _gaze = null;
        Clock.Restart();
        _last = 0;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _timer.Tick += VideoTick;
        _timer.Start();
    }

    private static void StopVideo()
    {
        if (_timer != null) { _timer.Stop(); _timer.Tick -= VideoTick; _timer = null; }
        if (_video != null) WebcamTracker.Instance.OnGazeMove -= OnGaze;
        _gaze = null;
        _video = null;
        _state = null;
        Clock.Reset();
        foreach (var (window, parent, light) in Lights) { window.Closed -= VideoClosed; parent.Children.Remove(light); }
        Lights.Clear();
    }

    private static void ClearVideoWindows()
    {
        foreach (var (window, parent, light) in Lights) { window.Closed -= VideoClosed; parent.Children.Remove(light); }
        Lights.Clear();
        foreach (var w in Windows.ToArray()) w.Close();
        Windows.Clear();
        _state = null;
    }

    private static void VideoTick(object? sender, EventArgs e)
    {
        try { AdvanceVideo(); }
        catch (Exception ex) { Serilog.Log.Warning(ex, "Lights Down: stopping after frame failure"); CloseAll(); }
    }

    private static void AdvanceVideo()
    {
        var video = _video;
        if (video == null) return;
        if (!video.Scheduler.IsPlaying || video.Surfaces.Count == 0) { CloseAll(); return; }
        if (!SuperAccess.IsOn(SuperEffect.LightsDown)) { ClearVideoWindows(); return; }
        if (_refused) return;
        if (_state == null && !OpenForVideo(video)) { ClearVideoWindows(); _refused = true; return; }
        var now = Clock.Elapsed.TotalSeconds;
        var dt = now - _last;
        _last = now;
        var settings = CoreSettings.Current;
        _state!.Tuning = LightsDownOptions.Tuning(settings.LightsDownDarkness, settings.LightsDownDimSpeed, settings.LightsDownReturnSpeed);
        var motion = AmbientFxCanvas.Env.Level switch { MotionLevel.Off => LightsDownMotion.Off, MotionLevel.Reduced => LightsDownMotion.Reduced, _ => LightsDownMotion.Full };
        var frame = _state.Step(dt, IsAttended(video), motion, settings.LockdownPhotosafe || GentleVisuals.On);
        Apply(frame);
        foreach (var (_, _, light) in Lights) light.SetFrame(frame);
    }

    private static bool OpenForVideo(MandatoryVideoOverlay video)
    {
        var first = video.Surfaces[0].Window;
        var screens = ScreenList.Enumerate(first);
        if (screens.Count == 0) return false;
        foreach (var screen in screens)
            if (!TryOpen(first, screen.Bounds, screen.Scaling)) return false;
        foreach (var s in video.Surfaces)
        {
            if (s.Picture.Parent is not Panel parent) return false;
            var light = new VideoLight(s.Video) { IsHitTestVisible = false, Focusable = false };
            parent.Children.Insert(parent.Children.IndexOf(s.Picture) + 1, light);
            Lights.Add((s.Window, parent, light));
            s.Window.Closed += VideoClosed;
        }
        _state = new LightsDownState();
        _last = Clock.Elapsed.TotalSeconds;
        return true;
    }

    private static void VideoClosed(object? sender, EventArgs e) => CloseAll();

    private static void OnGaze(Point point)
    {
        var dpi = WebcamTracker.Instance.Calibration?.MonitorBounds?.DpiScale ?? 1;
        if (dpi <= 0.25 || dpi >= 8) dpi = 1;
        var desktop = GazeFocus.GazeToDesktop(point);
        _gaze = new Point(desktop.X * dpi, desktop.Y * dpi);
        _gazeAt = Clock.Elapsed.TotalSeconds;
    }

    private static bool IsAttended(MandatoryVideoOverlay video)
    {
        if (video.GracePaused || video.PrimaryPaused) return false;
        var tracker = WebcamTracker.Instance;
        bool live = tracker.IsRunning && tracker.Calibration != null && _gaze != null && Clock.Elapsed.TotalSeconds - _gazeAt < 0.5;
        var cursor = X11Pointer.Read()?.At;
        if (!live && cursor == null) return false;
        var point = live ? _gaze!.Value : new Point(cursor!.Value.X, cursor.Value.Y);
        foreach (var s in video.Surfaces)
        {
            var p = s.Video.PointToScreen(default);
            var scale = s.Window.RenderScaling;
            var pic = VideoLight.Picture(s.Video.Bounds.Size, s.Video.Source);
            if (point.X >= p.X + pic.X * scale && point.X <= p.X + pic.Right * scale
                && point.Y >= p.Y + pic.Y * scale && point.Y <= p.Y + pic.Bottom * scale) return true;
        }
        return false;
    }

    // The LibVLC frame is an Avalonia Image, so the layer can sit above its background without
    // touching the video window's input. WPF LightsDownOverlay.cs:122-250 excludes the picture.
    internal sealed class VideoLight : Control
    {
        private readonly Image _video;
        internal LightsDownFrame Frame { get; private set; }
        internal VideoLight(Image video) => _video = video;
        internal void SetFrame(LightsDownFrame frame) { Frame = frame; InvalidateVisual(); }
        internal static Rect Picture(Size size, IImage? image)
        {
            var aspect = image is Bitmap bitmap && bitmap.PixelSize.Height > 0 ? (double)bitmap.PixelSize.Width / bitmap.PixelSize.Height : 0;
            var r = LightsDownMath.FitPicture(size.Width, size.Height, aspect);
            return new Rect(r.X, r.Y, r.W, r.H);
        }
        public override void Render(DrawingContext context)
        {
            base.Render(context);
            var r = Picture(Bounds.Size, _video.Source);
            var dim = new SolidColorBrush(Color.FromArgb((byte)(255 * Math.Clamp(Frame.DimAlpha, 0, 1)), 3, 1, 9));
            context.DrawRectangle(dim, null, new Rect(0, 0, Bounds.Width, r.Top));
            context.DrawRectangle(dim, null, new Rect(0, r.Bottom, Bounds.Width, Math.Max(0, Bounds.Height - r.Bottom)));
            context.DrawRectangle(dim, null, new Rect(0, r.Top, r.Left, r.Height));
            context.DrawRectangle(dim, null, new Rect(r.Right, r.Top, Math.Max(0, Bounds.Width - r.Right), r.Height));
            var glow = new SolidColorBrush(Color.FromArgb((byte)(255 * Math.Clamp(Frame.GlowAlpha * Frame.GlowPulse, 0, 1)), 255, 100, 210));
            context.DrawRectangle(null, new Pen(glow, 3), r, 10, 10);
            if (Frame.SplitAlpha > 0)
            {
                var split = new SolidColorBrush(Color.FromArgb((byte)(255 * Math.Clamp(Frame.SplitAlpha, 0, 1)), 255, 0, 90));
                context.DrawRectangle(null, new Pen(split, 3), r.Inflate(4), 10, 10);
            }
            if (Frame.StreakAlpha > 0)
            {
                var streak = new SolidColorBrush(Color.FromArgb((byte)(255 * Math.Clamp(Frame.StreakAlpha, 0, 1)), 255, 215, 240));
                context.DrawRectangle(streak, null, new Rect(r.X, r.Center.Y, r.Width, 4));
            }
        }
    }
}
