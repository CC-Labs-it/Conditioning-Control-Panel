using System;
using Avalonia.Threading;
using ConditioningControlPanel.Services.Webcam;
using OpenCvSharp;

namespace ConditioningControlPanel.Avalonia.Platform;

internal sealed partial class WebcamTracker
{
    private readonly TongueDetector _tongue = new();
    public event Action? OnTongueOut;

    // WPF inner-lip polygon, in source-frame coordinates. No frame or pixels leave this method.
    private static readonly int[] LipIndices =
        { 13, 312, 311, 310, 415, 308, 324, 318, 402, 317, 14, 87, 178, 88, 95, 78, 191, 80, 81, 82 };

    /// <summary>Tests call it bare; an idle tracker never delivers (same rule as the mouth event).</summary>
    internal bool ProcessTongue(Mat bgr, float[][] lm, DateTime now) => ProcessTongue(bgr, lm, now, _run);

    /// <summary>WPF: tongue HSV only while Core's MAR detector holds the mouth open; the frame's own run
    /// tags the event, so a stop drops it before delivery.</summary>
    private bool ProcessTongue(Mat bgr, float[][] lm, DateTime now, Run? run)
    {
        double ratio = _mouth.IsOpen ? TongueRatio(bgr, lm) : 0;
        bool fired = _tongue.Update(_mouth.IsOpen, ratio, now);
        if (fired) Dispatcher.UIThread.Post(() => { if (run != null && ReferenceEquals(_run, run)) OnTongueOut?.Invoke(); });
        return fired;
    }

    internal static double TongueRatio(Mat bgr, float[][] lm)
    {
        var points = new Point[LipIndices.Length];
        int xmin = int.MaxValue, ymin = int.MaxValue, xmax = int.MinValue, ymax = int.MinValue;
        for (int i = 0; i < points.Length; i++)
        {
            int x = (int)Math.Round(lm[LipIndices[i]][0]), y = (int)Math.Round(lm[LipIndices[i]][1]);
            points[i] = new Point(x, y);
            xmin = Math.Min(xmin, x); ymin = Math.Min(ymin, y);
            xmax = Math.Max(xmax, x); ymax = Math.Max(ymax, y);
        }
        xmin = Math.Max(0, xmin); ymin = Math.Max(0, ymin);
        xmax = Math.Min(bgr.Width - 1, xmax); ymax = Math.Min(bgr.Height - 1, ymax);
        int w = xmax - xmin + 1, h = ymax - ymin + 1;
        if (w < 4 || h < 4) return 0;
        for (int i = 0; i < points.Length; i++) points[i] = new Point(points[i].X - xmin, points[i].Y - ymin);
        using var mask = new Mat(h, w, MatType.CV_8UC1, Scalar.All(0));
        Cv2.FillPoly(mask, new[] { points }, Scalar.All(255));
        using var crop = new Mat(bgr, new Rect(xmin, ymin, w, h));
        using var hsv = new Mat();
        Cv2.CvtColor(crop, hsv, ColorConversionCodes.BGR2HSV);
        var pixels = new byte[w * h * 3];
        var inside = new byte[w * h];
        System.Runtime.InteropServices.Marshal.Copy(hsv.Data, pixels, 0, pixels.Length);
        System.Runtime.InteropServices.Marshal.Copy(mask.Data, inside, 0, inside.Length);
        int tongue = 0, other = 0;
        for (int i = 0; i < inside.Length; i++)
        {
            if (inside[i] == 0) continue;
            byte hue = pixels[i * 3], sat = pixels[i * 3 + 1], val = pixels[i * 3 + 2];
            if (val < 40 || (val >= 200 && sat <= 50)) continue; // shadow, then teeth
            if ((hue <= 20 || hue >= 160) && sat >= 30 && val >= 40) tongue++;
            else other++;
        }
        return tongue + other < 8 ? 0 : (double)tongue / (tongue + other);
    }
}
