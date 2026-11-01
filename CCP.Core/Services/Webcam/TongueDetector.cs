using System;
using OpenCvSharp;
namespace ConditioningControlPanel.Services.Webcam;
/// <summary>Shared WPF tongue timing and HSV classification. Face loss keeps cooldown. Capture-thread only.</summary>
public sealed class TongueDetector
{
    private DateTime? _since;
    private DateTime _last = DateTime.MinValue;
    private bool _out;
    public bool IsOut => _out;
    public double HeldMilliseconds(DateTime now) => _since is { } start ? (now - start).TotalMilliseconds : 0;
    public void FaceMissing() { _out = false; _since = null; }
    public bool Update(bool mouthOpen, double ratio, DateTime now)
    {
        if (!mouthOpen) { FaceMissing(); return false; }
        bool visible = ratio > (_out ? 0.18 : 0.28);
        bool fire = false;
        if (visible && !_out) _since = now;
        else if (visible && _since is { } start && _last < start
                 && (now - start).TotalMilliseconds >= 350
                 && (now - _last).TotalMilliseconds >= 700)
        { _last = now; fire = true; }
        else if (!visible && _out) _since = null;
        _out = visible;
        return fire;
    }
    private static readonly int[] LipIndices = { 13, 312, 311, 310, 415, 308, 324, 318, 402, 317, 14, 87, 178, 88, 95, 78, 191, 80, 81, 82 };
    public static double Ratio(Mat bgr, float[][] lm, out int tongue, out int teeth, out int shadow, out int other)
    {
        tongue = teeth = shadow = other = 0;
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
        using var hsv = new Mat(); Cv2.CvtColor(crop, hsv, ColorConversionCodes.BGR2HSV);
        byte[] pixels = new byte[w * h * 3], inside = new byte[w * h];
        System.Runtime.InteropServices.Marshal.Copy(hsv.Data, pixels, 0, pixels.Length);
        System.Runtime.InteropServices.Marshal.Copy(mask.Data, inside, 0, inside.Length);
        for (int i = 0; i < inside.Length; i++)
        {
            if (inside[i] == 0) continue;
            byte hue = pixels[i * 3], sat = pixels[i * 3 + 1], val = pixels[i * 3 + 2];
            if (val < 40) { shadow++; continue; }
            if (val >= 200 && sat <= 50) { teeth++; continue; }
            if ((hue <= 20 || hue >= 160) && sat >= 30 && val >= 40) tongue++;
            else other++;
        }
        return tongue + other < 8 ? 0 : (double)tongue / (tongue + other);
    }
}
