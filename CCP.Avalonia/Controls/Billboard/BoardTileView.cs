// PORTED from ConditioningControlPanel/Controls/Billboard/BoardTileView.cs: the Tonight Board's
// raised 3D tiles. The tile maths and the software raster are shared (CCP.Core BoardScene,
// BoardRaster); this view only owns the clock and blits the raster into one WriteableBitmap, as WPF.
// The clock is the window's shared 30 fps BeatLoop (WPF FrameClock at 30 fps); its time source is
// BeatLoop.Time, so tests step it. The board card's provider (WPF BoardProvider and BoardService)
// is not on this head yet: the view is registered and waits for it.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Services.Billboard;
using ConditioningControlPanel.Services.Billboard.Board;
using Env = ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env;

namespace ConditioningControlPanel.Avalonia.Controls.Billboard
{
    /// <summary>WPF BoardArtData (BoardProvider.cs:8): what the board card's art view receives, the
    /// picture and who to tell once it is on screen.</summary>
    public sealed record BoardArtData(BoardPicture Picture, Action<int>? OnShown);

    /// <summary>
    /// WPF BoardTileView: the pixel board as raised 3D tiles. Every frame is drawn in software into
    /// one bitmap sized to the art area at a whole tile pitch, on a 30 fps clock that runs only while
    /// the deck has played it (or a touch ripple is still spreading). One Image, no visual per tile.
    /// A board with nothing moving left (no effects, one frame, no ripple, landed) stops drawing.
    /// </summary>
    public sealed class BoardTileView : Panel, IBillboardArtView
    {
        /// <summary>WPF BoardArtRegistration.Key.</summary>
        public const string ArtKey = "board";

        private readonly BoardPicture? _picture;
        private readonly Action<int>? _onShown;
        private readonly BoardFxSet _fx;
        private readonly Image _image;
        private readonly BeatLoop _clock;
        private readonly List<BoardRipple> _ripples = new();
        private readonly int[] _colour = new int[BoardPicture.Tiles];
        private readonly float[] _lift = new float[BoardPicture.Tiles];
        private readonly BoardTileRole[] _role = new BoardTileRole[BoardPicture.Tiles];

        private BoardRaster? _raster;
        private WriteableBitmap? _bitmap;
        private readonly long _wall = BeatLoop.Time.GetTimestamp(); // ripple clock: always runs
        private long? _watch;             // effect clock start while playing (WPF Stopwatch _watch)
        private double _fxBank;           // effect seconds banked across pauses
        private double? _arrivalStart;    // effect time the arrival began; null = never played (a flat, still board)
        private bool _playing;            // the deck wants motion
        private bool _ambient;            // effects and strip frames may loop (Full motion, a tier that allows it)
        private bool _released;
        private bool _shownReported;
        private bool _dirty = true;

        public BoardTileView(object? data)
        {
            switch (data)
            {
                case BoardArtData d: _picture = d.Picture; _onShown = d.OnShown; break;
                case BoardPicture p: _picture = p; break;
            }
            _fx = BoardFxSet.From(_picture?.Post.Fx);

            Background = new SolidColorBrush(Color.FromRgb(0x04, 0x03, 0x0A));
            ClipToBounds = true;
            _image = new Image { Stretch = Stretch.Uniform };
            RenderOptions.SetBitmapInterpolationMode(_image, BitmapInterpolationMode.None);
            Children.Add(_image);

            _clock = new BeatLoop(this, _ => OnTick());
            SizeChanged += (_, _) => EnsureSurface();
            Loaded += (_, _) => OnLoaded();
            Unloaded += (_, _) => _clock.Stop();
        }

        /// <summary>Effect seconds: they run only while the deck has the card playing.</summary>
        private double FxNow => _fxBank + (_watch is { } w ? BeatLoop.Time.GetElapsedTime(w).TotalSeconds : 0);

        private double WallNow => BeatLoop.Time.GetElapsedTime(_wall).TotalSeconds;

        /// <summary>True while the frame clock runs (tests).</summary>
        internal bool Ticking => _clock.IsRunning;

        /// <summary>The bitmap on screen (tests).</summary>
        internal WriteableBitmap? Surface => _bitmap;

        private void OnLoaded()
        {
            if (!_shownReported && _picture != null)
            {
                _shownReported = true;
                try { _onShown?.Invoke(_picture.Post.Version); } catch (Exception) { }
            }
            EnsureSurface();
            UpdateClock();
        }

        // ---- IBillboardArtView -----------------------------------------------------------------

        void IBillboardArtView.Run() => Play();

        void IBillboardArtView.Hold() => Pause();

        public void Play()
        {
            if (_released || _picture == null) return;
            if (!Env.AllowTransitions) { Pause(); return; }
            _playing = true;
            _ambient = Env.AllowAmbientLoops;
            _watch ??= BeatLoop.Time.GetTimestamp();
            // The first Play is the arrival: tiles pop up and land one by one, then the effects roll.
            _arrivalStart ??= FxNow;
            _dirty = true;
            UpdateClock();
        }

        public void Pause()
        {
            _playing = false;
            if (_watch is { } w)
            {
                _fxBank += BeatLoop.Time.GetElapsedTime(w).TotalSeconds;
                _watch = null;
            }
            // A board paused half built would look broken: let every tile land at once.
            if (_arrivalStart is { } a && FxNow - a < BoardFxMath.BuildDoneSeconds)
                _arrivalStart = FxNow - BoardFxMath.BuildDoneSeconds;
            _dirty = true;
            Draw();
            UpdateClock();
        }

        public void Release()
        {
            _released = true;
            _playing = false;
            _clock.Stop();
            _watch = null;
            _ripples.Clear();
            _image.Source = null;
            _bitmap?.Dispose();
            _bitmap = null;
            _raster = null;
        }

        /// <summary>WPF Touch: a press on the board ripples from that tile, even while the deck holds
        /// the card paused under the pointer.</summary>
        public void Touch(Point normalized)
        {
            if (_released || _picture == null || !Env.AllowTransitions) return;
            if (!TryTileAt(normalized, out var tx, out var ty)) return;
            // A touch on a board that never played lands it first, so the ripple has tiles to lift.
            _arrivalStart ??= FxNow - BoardFxMath.BuildDoneSeconds;
            _ripples.Add(new BoardRipple(tx, ty, WallNow));
            if (_ripples.Count > 6) _ripples.RemoveAt(0);
            _dirty = true;
            UpdateClock();
        }

        // ---- clock ---------------------------------------------------------------------------

        /// <summary>The clock runs only while something moves: an effect, a strip, the arrival or a ripple.</summary>
        private void UpdateClock()
        {
            bool run = !_released && IsLoaded && (Animating || _ripples.Count > 0);
            if (run) _clock.Start(); else _clock.Stop();
        }

        private void OnTick()
        {
            if (_released) { _clock.Stop(); return; }
            double wall = WallNow;
            bool hadRipples = _ripples.Count > 0;
            for (int k = _ripples.Count - 1; k >= 0; k--)
                if (wall - _ripples[k].StartSeconds > BoardFxMath.RippleDropAfter) _ripples.RemoveAt(k);
            if (hadRipples && _ripples.Count == 0) _dirty = true; // draw the settled frame once
            Draw();
            UpdateClock();
        }

        // ---- drawing -------------------------------------------------------------------------

        private void EnsureSurface()
        {
            double w = Bounds.Width, h = Bounds.Height;
            if (_released || w <= 0 || h <= 0) return;
            double scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
            // Fit by whichever side is tighter so the 16:9 grid never overflows the art area.
            double px = Math.Min(w * scale, h * scale * BoardPicture.GridW / BoardPicture.GridH);
            int pitch = BoardTileLayout.PitchFor(px);
            if (_raster == null || _raster.Layout.Pitch != pitch)
            {
                _raster = new BoardRaster(pitch);
                // The message glow is baked once here, per picture and pitch; frames only scale it.
                _raster.SetPicture(_picture);
                _bitmap?.Dispose();
                _bitmap = new WriteableBitmap(new PixelSize(_raster.Width, _raster.Height), new Vector(96 * scale, 96 * scale),
                    PixelFormat.Bgra8888, AlphaFormat.Premul);
                _image.Source = _bitmap;
                _dirty = true;
            }
            Draw();
        }

        private bool Animating =>
            _playing && ((_ambient && (_fx.Animates || (_picture?.FrameCount ?? 1) > 1)) ||
                         (_arrivalStart is { } a && FxNow - a < BoardFxMath.BuildDoneSeconds));

        /// <summary>WPF Render (:184-205).</summary>
        private void Draw()
        {
            if (_raster == null || _bitmap == null || _picture == null) return;
            if (!Animating && _ripples.Count == 0 && !_dirty) return;
            _dirty = false;

            double t = FxNow;
            bool still = _arrivalStart == null;
            var fx = _ambient ? _fx : default;
            int frame = 0;
            if (_picture.FrameCount > 1 && _ambient && !still)
                frame = (int)(Math.Floor(t * _picture.Post.Fps) % _picture.FrameCount);
            double build = _arrivalStart is { } a ? t - a : BoardFxMath.BuildDoneSeconds;
            BoardScene.Compute(_picture, frame, fx, t, build, _ripples, WallNow, still, _colour, _lift, _role);
            // The glow breathes only on a clock that already runs for the effects (Full motion).
            bool breathe = _ambient && _playing && _fx.Animates;
            double glow = BoardFxMath.GlowStrength(t, build, still, breathe);
            // CRT is a look, not motion: it stays on a still board, only its flicker stops.
            _raster.Draw(_colour, _lift, _fx.Crt, _ambient && _playing ? BoardFxMath.CrtFlicker(t) : 0.012, _role, glow);
            using (var fb = _bitmap.Lock())
            {
                int w = _raster.Width, h = _raster.Height;
                if (fb.RowBytes == w * 4) Marshal.Copy(_raster.Pixels, 0, fb.Address, w * h);
                else for (int y = 0; y < h; y++) Marshal.Copy(_raster.Pixels, y * w, fb.Address + y * fb.RowBytes, w);
            }
            _image.InvalidateVisual();
        }

        /// <summary>A point in 0..1 view coordinates to a tile, through the Uniform letterbox.</summary>
        private bool TryTileAt(Point n, out double tx, out double ty)
        {
            tx = ty = 0;
            double vw = Bounds.Width, vh = Bounds.Height;
            if (vw <= 0 || vh <= 0) return false;
            return BoardTouch.TileAt(n, vw, vh, out tx, out ty);
        }
    }

    /// <summary>WPF BoardTouch: touch mapping, pure. The grid is drawn Uniform (16:9) and centred.</summary>
    public static class BoardTouch
    {
        public static bool TileAt(Point normalized, double viewW, double viewH, out double tx, out double ty)
        {
            tx = ty = 0;
            const double gw = BoardPicture.GridW, gh = BoardPicture.GridH;
            double s = Math.Min(viewW / gw, viewH / gh);
            if (s <= 0) return false;
            double ox = (viewW - gw * s) / 2, oy = (viewH - gh * s) / 2;
            double x = (normalized.X * viewW - ox) / s, y = (normalized.Y * viewH - oy) / s;
            if (x < 0 || y < 0 || x >= gw || y >= gh) return false;
            tx = Math.Floor(x);
            ty = Math.Floor(y);
            return true;
        }
    }
}
