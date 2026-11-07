// X11 click-through and stacking for the tube. See TubeInputShape.cs for why the input shape exists.
//
// Windows is untouched on purpose: there the Avalonia window keeps whatever hit-testing it had, and
// every native call below is X11-only (the X11Overlay entry points used here return false off X11).

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.AvatarTube
{
    public partial class AvatarTubeWindow
    {
        /// <summary>The input shape (window px) as last computed: only these take the mouse on X11.</summary>
        internal PixelRect[]? InputRects { get; private set; }

        /// <summary>Test seam: observes the region instead of sending it to an X server.</summary>
        internal Action<PixelRect[]>? InputShapeWriter { get; set; }

        private PixelRect[]? _appliedShape;
        private bool _shapePending;

        private void InitInputShape()
        {
            // Any layout pass is a candidate change: the chat input; a card; a new avatar pose; a
            // refit. Coalesced to one recompute per dispatcher turn and deduped against what the
            // server already has. The speech bubble is its own window with its own region
            // (AvatarTubeWindow.SpeechWindow.cs), so it never appears in this one.
            LayoutUpdated += (_, _) => RequestInputShape();
            Opened += (_, _) => { ApplyStacking(); RequestInputShape(); };
            if (_parentWindow != null) _parentWindow.Activated += OnParentActivated;
            // Before the first map: Avalonia's X11 backend creates the XID in the constructor, and a
            // window manager reads WM_TRANSIENT_FOR when it decides how to manage the window.
            ApplyStacking();
        }

        private void ReleaseInputShape()
        {
            if (_parentWindow != null) _parentWindow.Activated -= OnParentActivated;
        }

        /// <summary>WPF ApplyNativeOwner (Windowing.cs:621): attached, the tube is OWNED by main,
        /// so the window manager keeps it directly above main and moves both behind other apps
        /// together; detached it is unowned and Topmost (Windowing.cs:2282-2286). X11's ownership
        /// is WM_TRANSIENT_FOR. It is persistent, unlike the one-shot _NET_RESTACK_WINDOW, which
        /// is kept for window managers that ignore transient stacking.</summary>
        private void ApplyStacking()
        {
            var owner = _isAttached ? _parentWindow : null;
            X11Overlay.SetTransientFor(this, owner);
            if (owner != null && IsVisible) X11Overlay.RestackAbove(this, owner);
        }

        /// <summary>Main came to the front: the attached tube comes with it (WPF's owned window is
        /// raised by Windows with its owner). Restacks the tube only; it never activates.</summary>
        private void OnParentActivated(object? sender, EventArgs e)
        {
            if (_isAttached && IsVisible && _parentWindow != null) X11Overlay.RestackAbove(this, _parentWindow);
        }

        internal void RequestInputShape()
        {
            if (_shapePending) return;
            _shapePending = true;
            Dispatcher.UIThread.Post(ApplyInputShape, DispatcherPriority.Background);
        }

        /// <summary>Recomputes the region now and sends it if it changed.</summary>
        internal void ApplyInputShape()
        {
            _shapePending = false;
            try
            {
                if (!IsVisible) return;
                var backend = X11Overlay.BackendOf(TryGetPlatformHandle());
                if (backend == OverlayBackend.Win32 && InputShapeWriter is null) return;   // Windows: unchanged
                var shape = ComputeInputShape();
                InputRects = shape;
                if (TubeInputShape.SameRects(shape, _appliedShape)) return;
                if (InputShapeWriter is { } write) write(shape);
                else if (backend != OverlayBackend.X11 || !X11Overlay.SetInputRects(this, shape, shape.Length)) return;
                _appliedShape = shape;
            }
            catch (Exception ex) { Log.Debug(ex, "AvatarTube: input shape failed"); }
        }

        /// <summary>The painted parts of the tube, in window px: the tube art's alpha, then the box of
        /// every visible element drawn over it (chat input, title plate, Takeover bar, candle,
        /// Possession card; not the bubble, which has its own window). Attached it is also cut to the dock-side rect so her
        /// clear margin never sits over the shell (#1847).</summary>
        private PixelRect[] ComputeInputShape()
        {
            double s = RenderScaling > 0 ? RenderScaling : 1;
            var window = new PixelRect(0, 0, (int)Math.Ceiling(ClientSize.Width * s), (int)Math.Ceiling(ClientSize.Height * s));
            var rects = new List<PixelRect>(256);

            if (_imgTubeFrame.IsVisible && WindowBox(_imgTubeFrame, s) is { } slot)
            {
                if (_imgTubeFrame.Source is Bitmap art)
                {
                    var dest = TubeInputShape.UniformFit(new Size(art.PixelSize.Width, art.PixelSize.Height), slot);
                    if (TubeArtMask.For(art) is { } mask) TubeInputShape.AddArt(rects, mask, dest);
                    else TubeInputShape.AddBox(rects, dest);
                }
            }

            if (this.FindControl<Viewbox>("ContentViewbox")?.Child is Panel canvas)
                foreach (var child in canvas.Children)
                {
                    if (!child.IsVisible || child == _imgTubeFrame) continue;
                    if (child.Name == "TubeMotes") continue;   // decorative, inside the glass
                    if (child == _avatarBorder) { AddAvatar(rects, s); continue; }
                    if (WindowBox(child, s) is { } box) TubeInputShape.AddBox(rects, box);
                }

            return TubeInputShape.Finish(rects, window, _isAttached ? InputRect : null);
        }

        /// <summary>The avatar Border is margin-sized (most of the canvas), so it is her drawn layers
        /// that count, not the Border.</summary>
        private void AddAvatar(List<PixelRect> rects, double s)
        {
            if (!_avatarBorder.IsVisible || _avatarBorder.Child is not Panel host) return;
            foreach (var layer in host.Children)
            {
                if (!layer.IsVisible || layer is Image { Source: null }) continue;
                if (layer is Panel { Children.Count: 0 }) continue;
                if (WindowBox(layer, s) is { } box) TubeInputShape.AddBox(rects, box);
            }
        }

        /// <summary>A control's laid-out box mapped into the px of <paramref name="root"/> (this tube
        /// by default), including the Viewbox fit and any render transform; null before it has been
        /// laid out.</summary>
        private Rect? WindowBox(Visual control, double s, Visual? root = null)
        {
            var size = control.Bounds.Size;
            if (size.Width <= 0 || size.Height <= 0) return null;
            if (control.TransformToVisual(root ?? this) is not { } m) return null;
            var a = new Point(0, 0).Transform(m);
            var b = new Point(size.Width, 0).Transform(m);
            var c = new Point(0, size.Height).Transform(m);
            var d = new Point(size.Width, size.Height).Transform(m);
            double x0 = Math.Min(Math.Min(a.X, b.X), Math.Min(c.X, d.X)), x1 = Math.Max(Math.Max(a.X, b.X), Math.Max(c.X, d.X));
            double y0 = Math.Min(Math.Min(a.Y, b.Y), Math.Min(c.Y, d.Y)), y1 = Math.Max(Math.Max(a.Y, b.Y), Math.Max(c.Y, d.Y));
            return new Rect(x0 * s, y0 * s, (x1 - x0) * s, (y1 - y0) * s);
        }
    }
}
