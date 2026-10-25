using System;
using OpenCvSharp;

namespace ConditioningControlPanel.Services;

/// <summary>WPF inner-lip HSV heuristic and hold/cooldown rules. Capture-thread owned;
/// never retains a frame. Red inner-lip lipstick can false-fire, as in WPF.</summary>
public sealed class TongueOutDetector
{
        private static readonly int[] InnerLipPolygonIndices =
        {
            13, 312, 311, 310, 415, 308, 324, 318, 402, 317,
            14, 87, 178, 88, 95, 78, 191, 80, 81, 82
        };
        // Enter/leave ratios and the minimum hold were tightened together
        // (0.22/0.14/150ms → 0.28/0.18/350ms): with the looser values, pink
        // inner-lip pixels during ordinary talking cleared the ratio for a
        // couple of frames and fired constantly. A real deliberate tongue-out
        // shows a big pink area and is held — it clears all three easily.
        private const double TongueEnterRatio = 0.28;   // tongue_px / valid_px
        private const double TongueLeaveRatio = 0.18;
        private const int MinTongueOutMs = 350;
        private const int TongueCooldownMs = 700;
        // HSV bands (OpenCV: H ∈ [0,179], S/V ∈ [0,255]). Tuned loose because
        // consumer webcams often desaturate skin/tongue tones — a strict S>80
        // gate misclassifies real tongue pixels as "other". Sat/val floor was
        // 50/50 — bumped down because users with dim or color-corrected
        // cameras had the detector miss real protrusions entirely.
        private const int TongueHueLow1 = 0;
        private const int TongueHueHigh1 = 20;
        private const int TongueHueLow2 = 160;
        private const int TongueHueHigh2 = 179;
        private const int TongueMinSat = 30;
        private const int TongueMinVal = 40;
        private const int TeethMinVal = 200;            // bright + low sat = teeth (excluded)
        private const int TeethMaxSat = 50;
        private const int ShadowMaxVal = 40;            // very dark = shadow / mouth interior void (excluded)


        private bool _out;
        private DateTime? _since;
        private DateTime _lastFire = DateTime.MinValue;

        public void FaceMissing() { _out = false; _since = null; }
        public void Reset() { FaceMissing(); _lastFire = DateTime.MinValue; }

        public bool Update(bool mouthOpen, double ratio, DateTime now)
        {
            if (!mouthOpen) { FaceMissing(); return false; }
            bool next = ratio > (_out ? TongueLeaveRatio : TongueEnterRatio);
            bool fired = false;
            if (next && !_out) _since = now;
            else if (next && _since is { } since && _lastFire < since
                && (now - since).TotalMilliseconds >= MinTongueOutMs
                && (now - _lastFire).TotalMilliseconds >= TongueCooldownMs)
            {
                _lastFire = now;
                fired = true;
            }
            else if (!next) _since = null;
            _out = next;
            return fired;
        }

        // Order is part of the WPF heuristic: shadow, teeth, tongue, other.
        public static int Classify(byte h, byte s, byte v)
        {
            if (v < ShadowMaxVal) return 0;
            if (v >= TeethMinVal && s <= TeethMaxSat) return 1;
            bool hue = (h >= TongueHueLow1 && h <= TongueHueHigh1)
                || (h >= TongueHueLow2 && h <= TongueHueHigh2);
            return hue && s >= TongueMinSat && v >= TongueMinVal ? 2 : 3;
        }

        public static double ComputeRatio(Mat bgr, float[][] landmarks,
            out int tongue, out int teeth, out int shadow, out int other)
        {
            tongue = teeth = shadow = other = 0;

            // Build polygon in source-frame pixel coords + bounding box.
            int n = InnerLipPolygonIndices.Length;
            var pts = new Point[n];
            int xMin = int.MaxValue, yMin = int.MaxValue, xMax = int.MinValue, yMax = int.MinValue;
            for (int i = 0; i < n; i++)
            {
                int px = (int)Math.Round(landmarks[InnerLipPolygonIndices[i]][0]);
                int py = (int)Math.Round(landmarks[InnerLipPolygonIndices[i]][1]);
                pts[i] = new Point(px, py);
                if (px < xMin) xMin = px;
                if (py < yMin) yMin = py;
                if (px > xMax) xMax = px;
                if (py > yMax) yMax = py;
            }

            // Clip bbox to source frame; bail if degenerate.
            xMin = Math.Max(0, xMin);
            yMin = Math.Max(0, yMin);
            xMax = Math.Min(bgr.Width - 1, xMax);
            yMax = Math.Min(bgr.Height - 1, yMax);
            int bbW = xMax - xMin + 1;
            int bbH = yMax - yMin + 1;
            if (bbW < 4 || bbH < 4) return 0.0;

            // Translate polygon into bbox-local coords for the mask.
            var localPts = new Point[n];
            for (int i = 0; i < n; i++)
                localPts[i] = new Point(pts[i].X - xMin, pts[i].Y - yMin);

            using var mask = new Mat(bbH, bbW, MatType.CV_8UC1, Scalar.All(0));
            Cv2.FillPoly(mask, new[] { localPts }, Scalar.All(255));

            using var crop = new Mat(bgr, new Rect(xMin, yMin, bbW, bbH));
            using var hsv = new Mat();
            Cv2.CvtColor(crop, hsv, ColorConversionCodes.BGR2HSV);

            // Walk pixels by raw byte access for speed (avoids per-pixel C# Mat
            // indexer calls). HSV is CV_8UC3, so 3 bytes per pixel, row-major.
            int total = bbW * bbH;
            var hsvBytes = new byte[total * 3];
            System.Runtime.InteropServices.Marshal.Copy(hsv.Data, hsvBytes, 0, hsvBytes.Length);
            var maskBytes = new byte[total];
            System.Runtime.InteropServices.Marshal.Copy(mask.Data, maskBytes, 0, maskBytes.Length);

            for (int i = 0; i < total; i++)
            {
                if (maskBytes[i] == 0) continue;
                byte h = hsvBytes[3 * i + 0];
                byte s = hsvBytes[3 * i + 1];
                byte v = hsvBytes[3 * i + 2];

                switch (Classify(h, s, v))
                {
                    case 0: shadow++; break;
                    case 1: teeth++; break;
                    case 2: tongue++; break;
                    default: other++; break;
                }
            }

            int valid = tongue + other;     // exclude teeth and shadow from the denominator
            if (valid < 8) return 0.0;      // too few useful pixels to trust the ratio
            return (double)tongue / valid;
        }

}
