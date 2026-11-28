using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using ConditioningControlPanel.SuperCore;

namespace ConditioningControlPanel.Avalonia.Views.Overlays;

/// <summary>
/// Lights Down's picture on this head, the twin of WPF <c>Services/Super/LightsDownOverlay.cs</c>: ONE small
/// premultiplied BGRA bitmap drawn by Core <see cref="LightsDownCanvas"/> (about 320 cells across),
/// stretched over the screen with the bilinear (Low) filter, which is also what softens every edge. No
/// gradient brush, no save-layer, no per-frame geometry; a frame is written only when
/// <see cref="LightsDownCanvas.Worth"/> says it changed, so a dim at rest never repaints.
/// Never takes input or focus. Hosted inside a full-screen video window (over the picture, under the
/// attention targets, the grace card and the buttons) or as the content of a floating dim window.
/// </summary>
internal sealed class LightsDownCanvasView : Control
{
    private readonly LightsDownCanvas _canvas;
    private readonly WriteableBitmap _bitmap;
    private readonly int[] _copy;
    private LightsDownLook _drawn = LightsDownLook.None;
    private bool _blank = true;

    internal LightsDownCanvasView(LightsDownScreen screen)
    {
        Screen = screen;
        _canvas = new LightsDownCanvas(screen.W, screen.H, screen.Dpi);
        _bitmap = new WriteableBitmap(new PixelSize(_canvas.Width, _canvas.Height), new Vector(96, 96),
            PixelFormat.Bgra8888, AlphaFormat.Premul);
        _copy = new int[_canvas.Pixels.Length];
        IsHitTestVisible = false;
        Focusable = false;
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.LowQuality);
    }

    /// <summary>Render sample (--render-all): a floating dim half grown, one haloed hole.</summary>
    internal LightsDownCanvasView() : this(new LightsDownScreen(0, 0, 480, 270, 1))
    {
        Width = 480;
        Height = 270;
        var hole = new[] { (1L, new LightsDownRect(150, 80, 180, 101), true) };
        for (int i = 0; i < 3; i++) Screen.Holes.Update(0.25, hole, 0);   // eased fully open
        Paint(LightsDownFloating.Look(0.7, 1, LightsDownTuning.Stock, LightsDownMotion.Full, Screen.ShortSidePx, 0xE84393),
            Screen.Holes.Current, true);
    }

    internal LightsDownScreen Screen { get; }
    internal LightsDownCanvas CanvasForTest => _canvas;
    internal bool Blank => _blank;

    /// <summary>Write a frame if it differs visibly from the last one. True when it wrote.</summary>
    internal bool Paint(in LightsDownLook look, IReadOnlyList<LightsDownHole> holes, bool holesChanged)
    {
        if (!look.Visible)
        {
            if (_blank) return false;
            _blank = true;
            _drawn = look;
            InvalidateVisual();
            return true;
        }
        if (!_blank && !LightsDownCanvas.Worth(_drawn, look, holesChanged)) return false;
        _canvas.Draw(look, holes);
        Buffer.BlockCopy(_canvas.Pixels, 0, _copy, 0, _copy.Length * 4);
        using (var fb = _bitmap.Lock())
        {
            int w = _canvas.Width, h = _canvas.Height;
            if (fb.RowBytes == w * 4) Marshal.Copy(_copy, 0, fb.Address, w * h);
            else for (int y = 0; y < h; y++) Marshal.Copy(_copy, y * w, fb.Address + y * fb.RowBytes, w);
        }
        _blank = false;
        _drawn = look;
        InvalidateVisual();
        return true;
    }

    public override void Render(DrawingContext context)
    {
        if (_blank) return;
        // Cell centres land on the screen pixels they were sampled at: the picture is a whole number
        // of cells, a hair larger than the screen, and the host clips the rest.
        double scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 0;
        if (scale <= 0) scale = Screen.Dpi;
        double cell = _canvas.Cell / scale;
        context.DrawImage(_bitmap, new Rect(0, 0, _canvas.Width, _canvas.Height),
            new Rect(0, 0, _canvas.Width * cell, _canvas.Height * cell));
    }
}
