using System.IO;
using Avalonia.Media.Imaging;
using OpenCvSharp;

namespace ConditioningControlPanel.Avalonia.Platform;

internal static class AssetVideoThumbnail
{
    // WPF MainWindow.Assets.cs:910-948: a silent 100 px thumbnail, failure leaves the
    // placeholder. OpenCV is already shipped by this head; its file decoder replaces Shell API.
    internal static Bitmap? Decode(string path)
    {
        try
        {
            using var capture = new VideoCapture(path, VideoCaptureAPIs.FFMPEG);
            using var frame = new Mat();
            if (!capture.IsOpened() || !capture.Read(frame) || frame.Empty()) return null;
            using var small = new Mat();
            Cv2.Resize(frame, small, new Size(100, System.Math.Max(1,
                (int)System.Math.Round(frame.Height * 100.0 / frame.Width))));
            using var png = new MemoryStream(small.ToBytes(".png"));
            return new Bitmap(png);
        }
        catch { return null; }
    }
}
