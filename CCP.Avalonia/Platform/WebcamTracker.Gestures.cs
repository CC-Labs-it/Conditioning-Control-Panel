using System;
using Avalonia.Threading;
using ConditioningControlPanel.Services.Webcam;
using OpenCvSharp;

namespace ConditioningControlPanel.Avalonia.Platform;

internal sealed partial class WebcamTracker
{
    private readonly MouthGestures _mouth = new();
    public event Action? OnMouthOpen;
    public event Action? OnTongueOut;

    // WPF inner-lip polygon, in source-frame coordinates. No frame or pixels leave this method.
    private static readonly int[] LipIndices =
        { 13, 312, 311, 310, 415, 308, 324, 318, 402, 317, 14, 87, 178, 88, 95, 78, 191, 80, 81, 82 };

    internal static double MouthAspectRatio(float[][] lm)
    {
        static double Distance(float[] a, float[] b) => Math.Sqrt(Math.Pow(a[0] - b[0], 2) + Math.Pow(a[1] - b[1], 2));
        double width = Distance(lm[78], lm[308]);
        return width > 1e-6 ? (Distance(lm[81], lm[178]) + Distance(lm[13], lm[14])
            + Distance(lm[311], lm[402])) / (3 * width) : 0;
    }

    // Shared by the capture path and synthetic-landmark tests; no camera needed.
    internal void ProcessMouth(Mat bgr, float[][] lm, DateTime now)
    {
        if (_mouth.UpdateMouth(MouthAspectRatio(lm), CoreSettings.Current.WebcamSensitivity, now))
            Dispatcher.UIThread.Post(() => OnMouthOpen?.Invoke());
        double ratio = _mouth.IsOpen ? TongueRatio(bgr, lm) : 0;
        if (_mouth.UpdateTongue(ratio, now)) Dispatcher.UIThread.Post(() => OnTongueOut?.Invoke());
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
