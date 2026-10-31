// WPF AvatarTubeWindow.Windowing.cs:1037-1155: alpha bounds and frame-only mirror.
using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.AvatarTube;

public partial class AvatarTubeWindow
{
    private Bitmap? _artBoundsSource;
    private int _artMinCol, _artMaxCol;
    private bool _tubeArtFlipped;

    // Layout and mod/style changes can alter both the bounds and the avatar-centred flip axis.
    private void OnTubeArtLayoutUpdated(object? sender, EventArgs e) => UpdatePosition();

    private double TubeFlipAxis()
    {
        if (_avatarBorder.Bounds.Width > 0 && _avatarBorder.Parent is Visual parent
            && _imgTubeFrame.Parent == parent)
            return _avatarBorder.Bounds.Center.X - _imgTubeFrame.Bounds.X;
        return _imgTubeFrame.Bounds.Width / 2;
    }

    private void ApplyTubeArtFlip(bool flip)
    {
        _tubeArtFlipped = flip;
        _imgTubeFrame.RenderTransformOrigin = new RelativePoint(TubeFlipAxis(), 0, RelativeUnit.Absolute);
        if (flip)
        {
            if (_imgTubeFrame.RenderTransform is not ScaleTransform { ScaleX: -1, ScaleY: 1 })
                _imgTubeFrame.RenderTransform = new ScaleTransform(-1, 1);
        }
        else if (_imgTubeFrame.RenderTransform != null) _imgTubeFrame.RenderTransform = null;
    }

    internal bool TryMeasureArtInsetsPx(out int left, out int right, out double axis)
    {
        left = right = 0;
        axis = TubePixelSize.Width / 2.0;
        if (_imgTubeFrame.Source is not Bitmap bitmap || !_imgTubeFrame.IsVisible
            || _imgTubeFrame.Bounds.Width <= 0 || _imgTubeFrame.Bounds.Height <= 0) return false;
        try
        {
            if (!ReferenceEquals(bitmap, _artBoundsSource))
            {
                using var staging = new WriteableBitmap(bitmap.PixelSize, new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
                using var frame = staging.Lock();
                bitmap.CopyPixels(frame);
                var row = new byte[bitmap.PixelSize.Width * 4];
                int min = int.MaxValue, max = -1;
                for (int y = 0; y < bitmap.PixelSize.Height; y++)
                {
                    Marshal.Copy(frame.Address + y * frame.RowBytes, row, 0, row.Length);
                    for (int x = 0; x < bitmap.PixelSize.Width; x++)
                    {
                        if (row[x * 4 + 3] <= 12) continue;
                        min = Math.Min(min, x);
                        max = Math.Max(max, x);
                    }
                }
                _artBoundsSource = bitmap;
                _artMinCol = min;
                _artMaxCol = max;
            }
            if (_artMaxCol < 0) return false;
            double w = _imgTubeFrame.Bounds.Width, h = _imgTubeFrame.Bounds.Height;
            double scale = Math.Min(w / bitmap.PixelSize.Width, h / bitmap.PixelSize.Height);
            double offset = (w - bitmap.PixelSize.Width * scale) / 2;
            // Undo our own render mirror before TranslatePoint, just as WPF undoes it before PointToScreen.
            double X(double x) => _tubeArtFlipped ? 2 * TubeFlipAxis() - x : x;
            var a = _imgTubeFrame.TranslatePoint(new Point(X(offset + _artMinCol * scale), h / 2), this);
            var b = _imgTubeFrame.TranslatePoint(new Point(X(offset + (_artMaxCol + 1) * scale), h / 2), this);
            if (a == null || b == null) return false;
            double artLeft = Math.Min(a.Value.X, b.Value.X), artRight = Math.Max(a.Value.X, b.Value.X);
            if (_avatarBorder.Bounds.Width > 0)
            {
                var al = _avatarBorder.TranslatePoint(default, this);
                var ar = _avatarBorder.TranslatePoint(new Point(_avatarBorder.Bounds.Width, 0), this);
                if (al != null && ar != null)
                {
                    artLeft = Math.Min(artLeft, al.Value.X);
                    artRight = Math.Max(artRight, ar.Value.X);
                    axis = (al.Value.X + ar.Value.X) / 2 * DesktopScaling;
                }
            }
            left = Math.Max(0, (int)Math.Floor(artLeft * DesktopScaling));
            right = Math.Max(0, (int)Math.Floor(TubePixelSize.Width - artRight * DesktopScaling));
            return artRight > artLeft;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Tube art bounds unavailable; using stock insets");
            return false;
        }
    }
}
