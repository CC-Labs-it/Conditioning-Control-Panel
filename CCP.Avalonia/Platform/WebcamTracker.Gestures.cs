using System;
using Avalonia.Threading;
using ConditioningControlPanel.Services.Webcam;
using OpenCvSharp;
namespace ConditioningControlPanel.Avalonia.Platform;
internal sealed partial class WebcamTracker
{
    private readonly TongueDetector _tongue = new();
    public event Action? OnTongueOut;
    internal bool ProcessTongue(Mat bgr, float[][] lm, DateTime now) => ProcessTongue(bgr, lm, now, _run);
    private bool ProcessTongue(Mat bgr, float[][] lm, DateTime now, Run? run)
    {
        double ratio = _mouth.IsOpen ? TongueRatio(bgr, lm) : 0;
        bool fired = _tongue.Update(_mouth.IsOpen, ratio, now);
        if (fired) Dispatcher.UIThread.Post(() => { if (run != null && ReferenceEquals(_run, run)) OnTongueOut?.Invoke(); });
        return fired;
    }
    internal static double TongueRatio(Mat bgr, float[][] lm) => TongueDetector.Ratio(bgr, lm, out _, out _, out _, out _);
}
