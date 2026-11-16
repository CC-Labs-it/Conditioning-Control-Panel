using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Flash;

namespace ConditioningControlPanel.Avalonia.Views.Overlays
{
    /// <summary>
    /// Super Flicker Deck on one flash window: the head half of WPF's compositor card
    /// (FlashLayer.cs:516-646 draws it, :929-958 steps it, :1085-1130 breaks it). The rules are Core
    /// <see cref="FlickerDeck"/> and <see cref="FlickerShatter"/>; this file only draws them. The
    /// window is grown by <see cref="DeckPadPx"/> on every edge so the lift, wobble, shadow and puff
    /// never clip, and only the picture keeps the mouse (X11 input shape, set by the host).
    /// </summary>
    internal sealed partial class FlashOverlayWindow
    {
        internal FlickerDeckState? Deck { get; private set; }
        internal FlickerShatterState? DeckShatter { get; private set; }
        internal FlickerSpark[]? DeckSparks { get; private set; }
        /// <summary>The next face, decoded and waiting for the flip to reach its edge.</summary>
        internal Bitmap? DeckPending { get; private set; }
        /// <summary>WPF FlashWindow.DeckBreak: this click reached BreakAt, so the pop breaks the card.</summary>
        internal bool DeckBreak { get; set; }
        internal string? SourcePath { get; private set; }
        internal int DeckPadPx { get; private set; }
        /// <summary>The part of the grown window that takes the mouse (window px): the picture and its glow.</summary>
        internal PixelRect[]? DeckInputPx { get; set; }
        internal double DeckPeekDx, DeckPeekDy;
        /// <summary>Test seams: the crack strokes, the sheen band, the puff and the break.</summary>
        internal Path? CrackLine { get; private set; }
        internal Rectangle? DeckSheen { get; private set; }
        internal Canvas? DeckSparkLayer { get; private set; }
        internal Canvas? DeckShards { get; private set; }
        internal Border? DeckShadow { get; private set; }

        /// <summary>WPF MotionFx.Level: the setting capped by the OS and Gentle Visuals (FlashLayer reads it per frame).</summary>
        internal static MotionLevel DeckLevel => ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env.Level;

        private readonly ScaleTransform _deckScale = new();
        private readonly RotateTransform _deckRotate = new();
        private readonly TranslateTransform _deckShift = new();
        private Bitmap? _deckFace;
        private string? _pendingPath;
        private System.Collections.Generic.List<Bitmap>? _pendingFrames;
        private TimeSpan _pendingDelay;
        private Canvas? _deckFx;
        private Path? _crackUnder;
        private double _crackAmount = -1, _crackReach = -1, _shadowLift = -1;
        private Size _crackFit;

        private static readonly Color SparkPink = Color.FromRgb(0xFF, 0x8F, 0xD0);
        private static readonly Color SparkDust = Color.FromRgb(0xE8, 0xDC, 0xE4);

        /// <summary>Make this flash a card (WPF FlashService.AttachDeck :2726). Grows the window by
        /// <paramref name="padPx"/> on each edge; the host places it with <see cref="GlowPadPx"/>.</summary>
        internal void AttachDeck(FlickerDeckState deck, int padPx, double scaling)
        {
            Deck = deck;
            var k = scaling > 0 ? scaling : 1;
            DeckPadPx = padPx;
            GlowPadPx += padPx;
            var m = _card.Margin;
            var d = padPx / k;
            _card.Margin = new Thickness(m.Left + d, m.Top + d, m.Right + d, m.Bottom + d);
            var group = (TransformGroup)_card.RenderTransform!;
            group.Children.Insert(0, _deckShift);
            group.Children.Insert(0, _deckRotate);
            group.Children.Insert(0, _deckScale);
            DeckShadow = new Border { IsHitTestVisible = false, CornerRadius = _glow.CornerRadius };
            _card.Children.Insert(0, DeckShadow);
            DeckSheen = new Rectangle
            {
                IsVisible = false,
                RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Relative),
                RenderTransform = new SkewTransform(-Math.Atan(0.12) * 180 / Math.PI, 0),   // FlashLayer.cs:1015 Slant
                Fill = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                    GradientStops =
                    {
                        new GradientStop(Color.FromArgb(0, 255, 236, 246), 0),
                        new GradientStop(Color.FromArgb(255, 255, 236, 246), 0.5),
                        new GradientStop(Color.FromArgb(0, 255, 236, 246), 1),
                    },
                },
            };
            _crackUnder = new Path { Stroke = Brushes.Black, IsVisible = false };
            CrackLine = new Path { Stroke = Brushes.White, IsVisible = false };
            _deckFx = new Canvas
            {
                ClipToBounds = true, IsHitTestVisible = false,
                HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Left,
                VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Top,
                Children = { DeckSheen, _crackUnder, CrackLine },
            };
            _picture.Children.Insert(_picture.Children.IndexOf(_image) + 1, _deckFx);
            _image.SizeChanged += (_, _) => LayoutDeckFx();
            ApplyDeckPose(FlickerDeck.Sample(deck, DeckLevel));
        }

        /// <summary>WPF FlashLayer.ClearDecks: the switch went off, the card is a plain flash where it is.</summary>
        internal void ClearDeck()
        {
            if (Deck == null) return;
            Deck = null;
            DeckBreak = false;
            DropDeckPending();
            ApplyDeckPose(new FlickerPose(1, 1, 0, 0, 0, 0, 0));
            if (DeckShadow != null) DeckShadow.IsVisible = false;
            if (_deckFx != null) _deckFx.IsVisible = false;
            DropDeckSparks();
        }

        /// <summary>A picture for the flip in flight, with its animation frames (<paramref name="frames"/>[0] is
        /// <paramref name="picture"/>) when it has them; false (caller disposes) when the flip no longer wants it.</summary>
        internal bool OfferDeckPicture(FlickerDeckState deck, Bitmap picture, string path,
            System.Collections.Generic.List<Bitmap>? frames = null, TimeSpan frameDelay = default)
        {
            if (_closed || IsFadingOut || Exit != null || !ReferenceEquals(Deck, deck) || deck is not { Busy: true, Swapped: false })
                return false;
            DropDeckPending();
            DeckPending = picture;
            (_pendingPath, _pendingFrames, _pendingDelay) = (path, frames, frameDelay);
            return true;
        }

        private void DropDeckPending()
        {
            if (_pendingFrames is { } frames) for (var i = 1; i < frames.Count; i++) frames[i].Dispose();
            _pendingFrames = null;
            DeckPending?.Dispose();
            DeckPending = null;
        }

        /// <summary>One frame of the card (WPF FlashLayer.StepDeck :929): step, then swap, puff and pose.
        /// True when the card asked to be raised this frame.</summary>
        internal bool StepDeck(double dt, MotionLevel level)
        {
            if (Deck is not { } deck) return false;
            var ev = Exit == null ? FlickerDeck.Step(deck, dt, DeckPending != null, level) : FlickerEvents.None;
            if ((ev & FlickerEvents.Swap) != 0 && DeckPending is { } next)
            {
                DeckPending = null;
                SwapFace(next, _pendingFrames, _pendingDelay);
                _pendingFrames = null;
                DeckSparks = SwapPuff(deck, level);
                BuildSparkLayer();
            }
            if ((ev & FlickerEvents.GaveUp) != 0) DropDeckPending();
            if (DeckSparks is { } puff && !FlickerShatter.StepSparks(puff, dt)) DropDeckSparks();
            DrawDeckSparks();
            if (Exit == null) ApplyDeckPose(FlickerDeck.Sample(deck, level));
            return (ev & FlickerEvents.Raise) != 0;
        }

        /// <summary>WPF ApplyDeckSwap (FlashService.cs:2918-2930) / FlashLayer :937-947: the new face replaces
        /// the old, whose GIF stops, and an animated face plays from its first frame.</summary>
        private void SwapFace(Bitmap next, System.Collections.Generic.List<Bitmap>? nextFrames, TimeSpan frameDelay)
        {
            _gifTimer?.Stop(); _gifTimer = null;
            if (_gifFrames is { } frames) for (var i = 1; i < frames.Count; i++) frames[i].Dispose();
            _gifFrames = null;
            GifFrameIndex = 0;
            _image.Source = next;
            if (nextFrames != null) Animate(nextFrames, frameDelay);
            _deckFace?.Dispose();
            _deckFace = next;
            SourcePath = _pendingPath;
            ClipCorners();
            LayoutDeckFx();
        }

        /// <summary>WPF FlashLayer.SwapPuff :982: the flip's puff, plus dust from the crack's impact point when it deepens.</summary>
        private FlickerSpark[] SwapPuff(FlickerDeckState deck, MotionLevel level)
        {
            var motes = FlickerDeck.CrackMotes(deck.Flips, deck.BreakAt, level);
            if (motes <= 0 || deck.Cracks.Length == 0 || deck.Cracks[0].Length < 2)
                return FlickerShatter.SwapSparks(level, FlashOverlay.Rng);
            var k = DesktopScaling > 0 ? DesktopScaling : 1;
            double w = Math.Max(1, _card.Bounds.Width * k), h = Math.Max(1, _card.Bounds.Height * k);
            return FlickerShatter.SwapSparks(level, FlashOverlay.Rng, motes,
                (deck.Cracks[0][0] - 0.5) * w, (deck.Cracks[0][1] - 0.5) * h);
        }

        /// <summary>The pose about the card's centre (WPF FlashLayer.cs:518-526), its shadow (:960),
        /// sheen (:1005) and hairline cracks (:1028).</summary>
        internal void ApplyDeckPose(FlickerPose p)
        {
            double w = _card.Bounds.Width, h = _card.Bounds.Height;
            _deckScale.ScaleX = p.ScaleX * p.Scale;
            _deckScale.ScaleY = p.Scale * p.SquashY;
            _deckRotate.Angle = p.WobbleRad * 180 / Math.PI;
            _deckShift.X = DeckPeekDx * p.PeekFrac * w;
            _deckShift.Y = DeckPeekDy * p.PeekFrac * w + p.RiseFrac * h;
            if (Deck == null) return;
            if (DeckShadow != null && Math.Abs(p.Lift - _shadowLift) > 0.01)
            {
                _shadowLift = p.Lift;
                DeckShadow.CornerRadius = _glow.CornerRadius;
                DeckShadow.BoxShadow = new BoxShadows(new BoxShadow
                {
                    OffsetY = 3 + 12 * p.Lift, Blur = 2 * (4 + 12 * p.Lift), Color = Color.FromArgb(166, 0, 0, 0),
                });
            }
            if (_deckFx is null || DeckSheen is null) return;
            var fw = _deckFx.Width;
            var fh = _deckFx.Height;
            DeckSheen.IsVisible = p.Sheen > 0.004 && fw > 0;
            if (DeckSheen.IsVisible)
            {
                var bw = fw * 0.22;
                DeckSheen.Width = bw;
                DeckSheen.Height = fh;
                DeckSheen.Opacity = Math.Clamp(p.Sheen, 0, 1);
                Canvas.SetLeft(DeckSheen, -bw + p.SheenPos * (fw + bw + 0.12 * fh));
            }
            DrawCracks(p.Crack, p.CrackReach);
        }

        /// <summary>The cracks and sheen sit on the letterboxed picture, like WPF's fit rect.</summary>
        private void LayoutDeckFx()
        {
            if (_deckFx is null || _image.Source is not { } src || _image.Bounds.Width <= 0) return;
            var box = _image.Bounds.Size;
            var fit = Stretch.Uniform.CalculateSize(box, src.Size);
            _deckFx.Width = fit.Width;
            _deckFx.Height = fit.Height;
            _deckFx.Margin = new Thickness((box.Width - fit.Width) / 2, (box.Height - fit.Height) / 2, 0, 0);
            _crackAmount = -1;
            if (Deck is { } deck) ApplyDeckPose(FlickerDeck.Sample(deck, DeckLevel));
        }

        /// <summary>WPF FlashLayer.DrawCracks: a barely-there dark under-stroke and a thin pale line,
        /// each hairline run out to <paramref name="reach"/> of its length from the impact point.</summary>
        private void DrawCracks(double amount, double reach)
        {
            if (Deck is not { } deck || CrackLine is null || _crackUnder is null || _deckFx is null) return;
            var fit = new Size(_deckFx.Width, _deckFx.Height);
            CrackLine.IsVisible = _crackUnder.IsVisible = amount > 0 && fit.Width > 0;
            if (!CrackLine.IsVisible || (amount == _crackAmount && reach == _crackReach && fit == _crackFit)) return;
            (_crackAmount, _crackReach, _crackFit) = (amount, reach, fit);
            var geo = new StreamGeometry();
            using (var g = geo.Open())
                foreach (var line in deck.Cracks)
                {
                    if (line.Length < 4) continue;
                    var segs = line.Length / 2 - 1;
                    var run = Math.Clamp(reach, 0, 1) * segs;
                    if (run <= 0) continue;
                    g.BeginFigure(new Point(line[0] * fit.Width, line[1] * fit.Height), false);
                    for (var i = 1; i <= segs && i - 1 < run; i++)
                    {
                        var f = Math.Min(1, run - (i - 1));
                        g.LineTo(new Point((line[i * 2 - 2] + (line[i * 2] - line[i * 2 - 2]) * f) * fit.Width,
                            (line[i * 2 - 1] + (line[i * 2 + 1] - line[i * 2 - 1]) * f) * fit.Height));
                    }
                    g.EndFigure(false);
                }
            var w = Math.Max(0.8, Math.Min(fit.Width, fit.Height) * 0.0028);
            _crackUnder.Data = CrackLine.Data = geo;
            _crackUnder.StrokeThickness = w * 1.8;
            _crackUnder.Opacity = Math.Clamp(0.22 * amount, 0, 1);
            CrackLine.StrokeThickness = w;
            CrackLine.Opacity = Math.Clamp(0.55 * amount, 0, 1);
        }

        // shortcut: plain white dots, not WPF's additive (SKBlendMode.Plus) blend; upgrade if the puff reads dim over bright pictures.
        private void BuildSparkLayer()
        {
            if (DeckSparkLayer != null) _root.Children.Remove(DeckSparkLayer);
            DeckSparkLayer = null;
            if (DeckSparks is not { Length: > 0 } sparks) return;
            DeckSparkLayer = new Canvas { IsHitTestVisible = false };
            foreach (var sp in sparks)
                DeckSparkLayer.Children.Add(new Ellipse { Fill = new SolidColorBrush(sp.Mote ? SparkDust : Colors.White) });
            _root.Children.Add(DeckSparkLayer);
        }

        /// <summary>WPF FlashLayer.DrawDeckPuff :1061: dots about the card's resting centre, unmoved by its pose.</summary>
        private void DrawDeckSparks()
        {
            if (DeckSparks is not { } sparks || DeckSparkLayer is not { } layer) return;
            var k = DesktopScaling > 0 ? DesktopScaling : 1;
            var b = _card.Bounds;
            double cx = b.X + b.Width / 2, cy = b.Y + b.Height / 2;
            for (var i = 0; i < sparks.Length; i++)
            {
                var sp = sparks[i];
                var r = sp.Mote ? 0.8 + 0.7 * sp.Alpha : 1 + 2.2 * sp.Alpha;
                var dot = layer.Children[i];
                dot.IsVisible = sp.Alpha > 0;
                dot.Width = dot.Height = 2 * r;
                dot.Opacity = Math.Clamp(sp.Mote ? sp.Alpha * 0.55 : sp.Alpha, 0, 1);
                Canvas.SetLeft(dot, cx + sp.X / k - r);
                Canvas.SetTop(dot, cy + sp.Y / k - r);
            }
        }

        private void DropDeckSparks()
        {
            DeckSparks = null;
            if (DeckSparkLayer != null) _root.Children.Remove(DeckSparkLayer);
            DeckSparkLayer = null;
        }

        private void DropDeckPictures()
        {
            DropDeckPending();
            _deckFace?.Dispose();
            _deckFace = null;
        }

        /// <summary>
        /// WPF FlashLayer.BeginDeckShatter :281: the final click breaks the picture as it rests into
        /// forty triangles with white and pink sparks, on a window grown to the monitor like the
        /// owned Shatter. False under Off motion (the plain cut: the caller closes).
        /// </summary>
        internal bool TryBeginDeckShatter(MotionLevel level, Random rng)
        {
            if (Deck is not { } deck || Exit != null || Shatter != null || DeckShatter != null || _image.Source is not { } source) return false;
            var (x, y, fit, scale, bounds) = PictureOnScreen(source);
            var state = FlickerShatter.Create(x - bounds.X + fit.Width * scale / 2, y - bounds.Y + fit.Height * scale / 2,
                fit.Width * scale, fit.Height * scale, FlickerDeck.Sample(deck, level).WobbleRad, level, rng);
            if (state.Done) return false;
            ClearDeck();
            DeckShatter = state;
            _lifetimeTimer?.Stop(); _lifetimeTimer = null;
            IsHitTestVisible = false;
            X11Overlay.SetClickThrough(this, true);
            Position = bounds.Position;
            Width = bounds.Width / scale;
            Height = bounds.Height / scale;
            _card.IsVisible = false;
            if (CreditMark != null) CreditMark.IsVisible = false;
            double w = state.W / scale, h = state.H / scale;
            DeckShards = new Canvas { IsHitTestVisible = false };
            foreach (var sh in state.Shards)
            {
                var tri = new StreamGeometry();
                using (var g = tri.Open())
                {
                    g.BeginFigure(new Point(sh.U0 * w, sh.V0 * h), true);
                    g.LineTo(new Point(sh.U1 * w, sh.V1 * h));
                    g.LineTo(new Point(sh.U2 * w, sh.V2 * h));
                    g.EndFigure(true);
                }
                var shard = new Border
                {
                    Name = "DeckShard", Width = w, Height = h,
                    Background = _look.HasGlow ? null : Brushes.Black,   // FlashLayer.cs:1106-1110
                    Child = new Image { Source = source, Stretch = Stretch.Fill },
                    Clip = tri,
                    RenderTransformOrigin = new RelativePoint(w / 2 + sh.Cx / scale, h / 2 + sh.Cy / scale, RelativeUnit.Absolute),
                    RenderTransform = new TransformGroup { Children = { new RotateTransform(), new TranslateTransform() } },
                };
                Canvas.SetLeft(shard, state.CenterX / scale - w / 2);
                Canvas.SetTop(shard, state.CenterY / scale - h / 2);
                DeckShards.Children.Add(shard);
            }
            foreach (var sp in state.Sparks)
                DeckShards.Children.Add(new Ellipse { Fill = new SolidColorBrush(sp.Pink ? SparkPink : Colors.White) });
            _root.Children.Add(DeckShards);
            DeckShatterFrame(0);
            StartExitClock();
            return true;
        }

        /// <summary>One frame of the break (WPF FlashLayer.DrawDeckShards :1085); closes when it is done.</summary>
        private bool DeckShatterFrame(double? fixedDt = null)
        {
            if (DeckShatter is not { } s || DeckShards is not { } panel) return false;
            var stamp = FlashOverlay.Clock.GetTimestamp();
            FlickerShatter.Step(s, fixedDt ?? FlashOverlay.Clock.GetElapsedTime(_lastExitTick, stamp).TotalSeconds);
            _lastExitTick = stamp;
            var k = DesktopScaling > 0 ? DesktopScaling : 1;
            var alpha = Math.Clamp(FlickerShatter.ShardAlpha(s.ElapsedSec), 0, 1);
            for (var i = 0; i < s.Shards.Length; i++)
            {
                var sh = s.Shards[i];
                var shard = panel.Children[i];
                var t = (TransformGroup)shard.RenderTransform!;
                ((RotateTransform)t.Children[0]).Angle = (sh.AngleRad + s.Rot0) * 180 / Math.PI;
                ((TranslateTransform)t.Children[1]).X = sh.X / k;
                ((TranslateTransform)t.Children[1]).Y = sh.Y / k;
                shard.Opacity = alpha;
            }
            for (var i = 0; i < s.Sparks.Length; i++)
            {
                var sp = s.Sparks[i];
                var dot = panel.Children[s.Shards.Length + i];
                var r = 1 + 2.2 * sp.Alpha;
                dot.IsVisible = sp.Alpha > 0;
                dot.Width = dot.Height = 2 * r;
                dot.Opacity = Math.Clamp(sp.Alpha, 0, 1);
                Canvas.SetLeft(dot, (s.CenterX + sp.X) / k - r);
                Canvas.SetTop(dot, (s.CenterY + sp.Y) / k - r);
            }
            SetDisplayedAlpha(_alpha);
            if (s.Done) { _exitTimer?.Stop(); _exitTimer = null; Close(); }
            return true;
        }
    }
}
