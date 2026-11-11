// PORTED from ConditioningControlPanel/AvatarTube/AvatarTubeWindow.Speech.cs:890-1216
// (ApplySpeechBubblePlacement, SyncSpeechPopupOpen, CreateBubbleWindow, BubbleWndProc,
// HookSpeechBubblePlacement) and Windowing.cs:1928-1975 (the bubble's band in ReassertTopmost,
// DemoteSpeechBubble): the speech bubble is its OWN window, not part of the tube's scaled canvas.
//
// Why a window (WPF Speech.cs:1060-1063): a plain non-topmost, non-activating tool window whose
// NATIVE owner is the tube sits directly above the tube, goes behind other apps with it, and can be
// placed anywhere on the monitor - beside her, off main's window - at its real size. Inside the
// tube's canvas the bubble was drawn over the glass and only as far as the tube window reached.
//
// Ownership: GWL_HWNDPARENT on Windows, WM_TRANSIENT_FOR on X11 (X11Overlay.SetOwner). Avalonia's
// own Owner is deliberately not used, as WPF does not use Window.Owner here: Avalonia only calls
// SetParent when Owner changes, so the native owner set below is never overwritten.
//
// Input: on X11 the window's own ShapeInput region is the bubble and its tail, so the transparent
// margin around it (room for the shadow and the tail) is click-through; X11 has no per-pixel hit
// test. On Windows the small window keeps all of its input, as WPF's layered bubble takes clicks on
// its shadow too. Either way the bubble no longer appears in the tube's region at all.
//
// ponytail: WPF also hides the bubble while a Chaos game window is up (ChaosWebViewHost.AnyGameActive,
// ccp-bugs #1345). This head has no game-active signal to listen to yet; add it to ShouldShowBubble
// when one exists.

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.AvatarTubeLayout;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.AvatarTube
{
    public partial class AvatarTubeWindow
    {
        internal const double SpeechBubbleMaxWidth = 340;       // WPF Speech.cs:1148: outer DIPs; text column about 300
        internal const double SpeechBubbleHistoryWidth = 460;   // WPF Speech.cs:1149: chat history panel

        private Grid _speechPopupRoot = null!;
        private Path _speechTail = null!;
        private readonly ScaleTransform _speechTailFlip = new(1, 1);
        private Window? _bubbleWindow;
        private bool _placingSpeechBubble;
        private string? _lastBubbleDecision;
        private PixelRect[]? _appliedBubbleShape;
        private bool _bubbleShapePending;

        /// <summary>The bubble's own window once the tube has opened (null before, and in a render).</summary>
        internal Window? BubbleWindow => _bubbleWindow;

        /// <summary>The last placement, physical px. Tests read it; nothing else does.</summary>
        internal BubblePlan? LastBubblePlan { get; private set; }

        /// <summary>The bubble window's input region (its window px) as last computed.</summary>
        internal PixelRect[]? BubbleInputRects { get; private set; }

        /// <summary>Test seam: observes the bubble window's region instead of sending it.</summary>
        internal Action<PixelRect[]>? BubbleShapeWriter { get; set; }

        /// <summary>Constructor half: lifts the bubble out of this window's tree (its names stay in
        /// this window's name scope) and wires the re-placement hooks of WPF HookSpeechBubblePlacement.</summary>
        private void InitSpeechWindow()
        {
            _speechPopupRoot = this.FindControl<Grid>("SpeechPopupRoot")!;
            _speechTail = this.FindControl<Path>("SpeechTail")!;
            _speechTail.RenderTransform = _speechTailFlip;
            if (_speechPopupRoot.Parent is Panel parked) parked.Children.Remove(_speechPopupRoot);
            _speechPopupRoot.IsVisible = true;

            _speechBubble.PropertyChanged += (_, e) =>
            {
                if (e.Property != IsVisibleProperty) return;
                if (_speechBubble.IsVisible) ApplySpeechBubblePlacement();
                SyncSpeechPopupOpen();
            };
            _inputPanel.PropertyChanged += (_, e) =>
            {
                if (e.Property == IsVisibleProperty && _speechBubble.IsVisible) ApplySpeechBubblePlacement();
            };
            _inputPanel.SizeChanged += (_, _) => { if (_inputPanel.IsVisible && _speechBubble.IsVisible) ApplySpeechBubblePlacement(); };
            // The typewriter growing it, a scroll bar appearing, chat history swapping in.
            _speechBubble.SizeChanged += (_, _) => { if (_speechBubble.IsVisible) ApplySpeechBubblePlacement(); };
            PositionChanged += (_, _) => { if (_speechBubble.IsVisible) ApplySpeechBubblePlacement(); };
            PropertyChanged += (_, e) =>
            {
                if (e.Property == IsVisibleProperty || e.Property == WindowStateProperty) SyncSpeechPopupOpen();
                else if (e.Property == ClientSizeProperty && _speechBubble.IsVisible) ApplySpeechBubblePlacement();
            };
            if (_parentWindow != null) _parentWindow.PropertyChanged += OnParentPropertyChangedForBubble;
            Opened += (_, _) => { CreateBubbleWindow(); SyncSpeechPopupOpen(); };
        }

        private void ReleaseSpeechWindow()
        {
            if (_parentWindow != null) _parentWindow.PropertyChanged -= OnParentPropertyChangedForBubble;
            try { _bubbleWindow?.Close(); } catch (Exception ex) { Log.Debug(ex, "AvatarTube: bubble window close"); }
            _bubbleWindow = null;
        }

        private void OnParentPropertyChangedForBubble(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            // WPF SpeechBubbleVisibility: attached, the bubble goes with main.
            if (e.Property == IsVisibleProperty || e.Property == WindowStateProperty) SyncSpeechPopupOpen();
        }

        /// <summary>WPF CreateBubbleWindow (Speech.cs:1071-1100): no frame, transparent, never
        /// activated, not in the taskbar, not topmost, sized to the bubble, natively owned by the tube.</summary>
        private void CreateBubbleWindow()
        {
            if (_bubbleWindow != null) return;
            var w = new Window
            {
                WindowDecorations = WindowDecorations.None,
                TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent },
                Background = Brushes.Transparent,
                ShowActivated = false,
                ShowInTaskbar = false,
                Topmost = false,
                CanResize = false,
                SizeToContent = SizeToContent.WidthAndHeight,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Focusable = false,
                Title = "",
                Content = _speechPopupRoot,
            };
            _bubbleWindow = w;
            // Windows: WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW (Speech.cs:1091-1092) through the overlay's
            // style callback, so Avalonia's style rebuilds keep them; every show slots it right above
            // the tube instead of raising the app (BubbleWndProc). Both no-ops off Windows.
            X11Overlay.SetPassive(w);
            Win32Overlay.KeepShowsAboveOwner(w, () =>
                TryGetPlatformHandle() is { HandleDescriptor: "HWND" } h ? h.Handle : IntPtr.Zero);
            ApplyBubbleStacking();
            w.LayoutUpdated += (_, _) => RequestBubbleShape();
            w.Opened += (_, _) => { ApplyBubbleStacking(); RequestBubbleShape(); };
            Log.Debug("AvatarTube: speech bubble window created");
        }

        /// <summary>The bubble window is owned by the tube, and rides in the tube's band: topmost while
        /// the tube is free (WPF ReassertTopmost lifts it above the tube, #1296), not topmost once
        /// attached (DemoteSpeechBubble). Re-applied before every show and on attach/detach.</summary>
        private void ApplyBubbleStacking()
        {
            if (_bubbleWindow is not { } w) return;
            X11Overlay.SetOwner(w, this);
            w.Topmost = !_isAttached;
        }

        /// <summary>WPF SpeechBubbleVisibility: the bubble window is up exactly while the bubble is meant
        /// to show and the tube (and, while attached, main) is on screen.</summary>
        internal bool ShouldShowBubble() => SpeechBubbleVisibility.ShouldShow(
            _speechBubble.IsVisible, IsVisible, WindowState == WindowState.Minimized,
            _isAttached, _parentWindow?.IsVisible ?? true, _parentWindow?.WindowState == WindowState.Minimized);

        /// <summary>WPF SyncSpeechPopupOpen (Speech.cs:1036-1058).</summary>
        private void SyncSpeechPopupOpen()
        {
            if (_bubbleWindow is not { } w) return;
            bool want = ShouldShowBubble();
            if (want == w.IsVisible) return;
            try
            {
                if (want)
                {
                    ApplyBubbleStacking();          // owner before the map: the WM reads it when it maps
                    ApplySpeechBubblePlacement();   // position the hidden window first: no flash at 0,0
                    w.Show();
                    ApplyBubbleStacking();
                    ApplySpeechBubblePlacement();
                    RequestBubbleShape();
                }
                else
                {
                    w.Hide();
                    _isMouseOverSpeechBubble = false;   // a hidden window never sends the PointerExited
                }
            }
            catch (Exception ex) { Log.Debug(ex, "AvatarTube: bubble window show/hide"); }
        }

        /// <summary>
        /// WPF ApplySpeechBubblePlacement (Speech.cs:902-1011). Everything is physical px; the maths is
        /// Core's <see cref="SpeechBubblePlacement"/>: inside the work area of the monitor she is on,
        /// never over main's window while attached (an opaque pixel over main swallows its clicks),
        /// above her head on the roomier side, tail aimed at her. The bubble window is the bubble plus
        /// its 16 DIP margin (room for the shadow and the tail).
        /// </summary>
        internal void ApplySpeechBubblePlacement()
        {
            if (_placingSpeechBubble) return;
            _placingSpeechBubble = true;
            try
            {
                if (_avatarBorder.Bounds.Width <= 0 || TopLevel.GetTopLevel(_avatarBorder) != this) return;

                // Avatar box in physical px.
                var a0 = _avatarBorder.PointToScreen(new Point(0, 0));
                var a1 = _avatarBorder.PointToScreen(new Point(_avatarBorder.Bounds.Width, _avatarBorder.Bounds.Height));
                var avatar = Box.FromEdges(Math.Min(a0.X, a1.X), Math.Min(a0.Y, a1.Y), Math.Max(a0.X, a1.X), Math.Max(a0.Y, a1.Y));

                if (_inputPanel.IsVisible && _inputPanel.Bounds.Width > 0 && _inputPanel.Bounds.Height > 0)
                {
                    var i0 = _inputPanel.PointToScreen(new Point(0, 0));
                    var i1 = _inputPanel.PointToScreen(new Point(_inputPanel.Bounds.Width, _inputPanel.Bounds.Height));
                    avatar = SpeechBubblePlacement.ClearOfInput(avatar, Box.FromEdges(
                        Math.Min(i0.X, i1.X), Math.Min(i0.Y, i1.Y), Math.Max(i0.X, i1.X), Math.Max(i0.Y, i1.Y)));
                }

                var screen = Screens.ScreenFromPoint(new PixelPoint((int)Math.Round(avatar.X + avatar.W / 2), (int)Math.Round(avatar.Y + avatar.H / 2)))
                             ?? Screens.ScreenFromWindow(this) ?? Screens.Primary;
                if (screen == null) return;
                var work = ToBox(screen.WorkingArea);

                Box? obstacle = null;
                if (_isAttached && _parentWindow is { IsVisible: true } p && p.WindowState != WindowState.Minimized)
                    obstacle = ToBox(new PixelRect(p.Position, PixelSize.FromSize(p.ClientSize, p.DesktopScaling)));

                // DIP -> px of the bubble's own window when it is up, else of the monitor she is on.
                double s = _bubbleWindow is { IsVisible: true } bw && bw.RenderScaling > 0 ? bw.RenderScaling
                         : screen.Scaling > 0 ? screen.Scaling : 1;

                var content = _speechBubble.Child as Control;
                double chromeW = _speechBubble.Padding.Left + _speechBubble.Padding.Right
                    + _speechBubble.BorderThickness.Left + _speechBubble.BorderThickness.Right;
                double chromeH = _speechBubble.Padding.Top + _speechBubble.Padding.Bottom
                    + _speechBubble.BorderThickness.Top + _speechBubble.BorderThickness.Bottom;
                double configured = double.IsFinite(_speechBubble.MaxWidth) && _speechBubble.MaxWidth > 0
                    ? _speechBubble.MaxWidth : SpeechBubbleMaxWidth;

                var plan = SpeechBubblePlacement.Place(work, work, obstacle, avatar,
                    configured * s, chromeW * s, outerMaxPx =>
                    {
                        double outerMax = outerMaxPx / s;
                        if (content == null) return (outerMaxPx, 80 * s);
                        double inner = Math.Max(0, outerMax - chromeW);
                        content.MaxWidth = inner;
                        content.Measure(new Size(inner, double.PositiveInfinity));
                        double width = Math.Max(_speechBubble.MinWidth, content.DesiredSize.Width + chromeW);
                        return (Math.Min(outerMax, width) * s, (content.DesiredSize.Height + chromeH) * s);
                    },
                    edgeGap: Math.Max(SpeechBubblePlacement.Gap, _speechBubble.Margin.Left * s + 2));

                if (content != null) content.MaxWidth = plan.ContentMaxWidth / s;
                LastBubblePlan = plan;

                double pad = _speechBubble.Margin.Left;
                var pos = new PixelPoint((int)Math.Round(plan.Bubble.X - pad * s), (int)Math.Round(plan.Bubble.Y - pad * s));
                if (_bubbleWindow is { } win && win.Position != pos) win.Position = pos;

                // Tail: a 14x9 wedge in the glass colour, tucked 1px into the bubble's edge.
                const double tailW = 14, tailH = 9;
                double bubbleH = plan.Bubble.H / s;
                double tailLeft = plan.TailX / s - plan.Bubble.X / s + pad - tailW / 2;
                double tailTop = plan.Tail == TailEdge.Bottom ? pad + bubbleH - 1 : pad - tailH + 1;
                _speechTail.Margin = new Thickness(tailLeft, tailTop, 0, 0);
                _speechTailFlip.ScaleY = plan.Tail == TailEdge.Bottom ? 1 : -1;

                string decision = SpeechBubblePlacement.LogKey(plan, avatar, work, obstacle, s);
                if (decision != _lastBubbleDecision)
                {
                    _lastBubbleDecision = decision;
                    Log.Debug("Speech bubble: rect=({X},{Y} {W}x{H}) tail={Tail} growsLeft={Left} avatar=({AX},{AY} {AW}x{AH}) main={Main} scale={S:F2}",
                        (int)plan.Bubble.X, (int)plan.Bubble.Y, (int)plan.Bubble.W, (int)plan.Bubble.H, plan.Tail, plan.GrowsLeft,
                        (int)avatar.X, (int)avatar.Y, (int)avatar.W, (int)avatar.H,
                        obstacle is Box m ? $"{(int)m.X},{(int)m.Y},{(int)m.Right},{(int)m.Bottom}" : "-", s);
                }
            }
            catch (Exception ex) { Log.Debug("ApplySpeechBubblePlacement failed: {Error}", ex.Message); }
            finally { _placingSpeechBubble = false; }
        }

        private void RequestBubbleShape()
        {
            if (_bubbleShapePending) return;
            _bubbleShapePending = true;
            Dispatcher.UIThread.Post(ApplyBubbleShape, DispatcherPriority.Background);
        }

        /// <summary>Only the bubble's painted area takes the mouse: its box and its tail, in the bubble
        /// window's px. Sent only when it changed.</summary>
        internal void ApplyBubbleShape()
        {
            _bubbleShapePending = false;
            try
            {
                if (_bubbleWindow is not { IsVisible: true } w) return;
                // Windows keeps the whole small window: WPF's layered bubble hit-tests its shadow's
                // pixels too, which is most of the margin, and a window region would clip the shadow.
                var backend = X11Overlay.BackendOf(w.TryGetPlatformHandle());
                if (backend == OverlayBackend.Win32 && BubbleShapeWriter is null) return;
                double s = w.RenderScaling > 0 ? w.RenderScaling : 1;
                var rects = new List<PixelRect>(2);
                if (_speechBubble.IsVisible && WindowBox(_speechBubble, s, w) is { } bubble) TubeInputShape.AddBox(rects, bubble);
                if (_speechTail.IsVisible && WindowBox(_speechTail, s, w) is { } tail) TubeInputShape.AddBox(rects, tail);
                var window = new PixelRect(0, 0, (int)Math.Ceiling(w.ClientSize.Width * s), (int)Math.Ceiling(w.ClientSize.Height * s));
                var shape = TubeInputShape.Finish(rects, window, null);
                BubbleInputRects = shape;
                if (shape.Length == 0 || TubeInputShape.SameRects(shape, _appliedBubbleShape)) return;   // not laid out yet
                if (BubbleShapeWriter is { } write) write(shape);
                else if (backend != OverlayBackend.X11 || !X11Overlay.SetInputRects(w, shape, shape.Length)) return;
                _appliedBubbleShape = shape;
            }
            catch (Exception ex) { Log.Debug(ex, "AvatarTube: bubble input shape failed"); }
        }
    }
}
