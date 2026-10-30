using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SkiaSharp;

namespace ConditioningControlPanel
{
    /// <summary>
    /// Plays ONE animated emote clip (animated WebP with real alpha, or GIF) into an <see cref="Image"/>,
    /// once, then holds the last frame and raises <see cref="Completed"/>. The tube's SkiaSharp lane next
    /// to XamlAnimatedGif (which is GIF only): Rain's clips are WebP. The WPF twin of the 7.2 Avalonia
    /// head's EmoteClipPlayer (feat/rain-avatar).
    ///
    /// <para>Memory: only the clip in hand is kept - its compressed bytes and the one
    /// <see cref="WriteableBitmap"/> the Image shows, which is also the compositing buffer. Frames decode
    /// on demand with <see cref="SKCodec"/>, honouring per-frame durations and <c>RequiredFrame</c>, so a
    /// 6 s clip never sits in memory as ninety bitmaps.</para>
    ///
    /// <para>Timing: a Render-priority timer calls <see cref="Tick"/> with a monotonic ms stamp; the
    /// player advances as many frames as the elapsed time covers and paints the last. Load decodes the
    /// first frame off the UI thread; everything after runs on the UI thread. A newer Load or Stop makes
    /// a load still in flight a no-op. The source is a pack:// URI or an absolute file (a mod's or a
    /// baked pack's own folder).</para>
    /// </summary>
    internal sealed class EmoteClipPlayer : IDisposable
    {
        private const int MinFrameMs = 20;
        private const int DefaultFrameMs = 100;   // a 0/10 ms GIF delay plays as browsers do

        private readonly Image _target;
        private readonly DispatcherTimer _timer;
        private int _generation;

        private SKCodec? _codec;
        private SKData? _data;
        private SKImageInfo _info;
        private int[] _durations = Array.Empty<int>();
        private int[] _required = Array.Empty<int>();
        private int _index;
        private int _bufferFrame;
        private long _frameStartMs;
        private long _pausedAtMs = -1;
        private WriteableBitmap? _bitmap;

        public EmoteClipPlayer(Image target)
        {
            _target = target ?? throw new ArgumentNullException(nameof(target));
            _timer = new DispatcherTimer(DispatcherPriority.Render, target.Dispatcher) { Interval = TimeSpan.FromMilliseconds(15) };
            _timer.Tick += (_, _) => Tick(Now());
        }

        /// <summary>The source last asked for (null after Stop).</summary>
        public Uri? Source { get; private set; }
        /// <summary>A fresh object per clip that reached its first frame: the crossfade's "new animator" test.</summary>
        public object? ReadyToken { get; private set; }
        public bool IsReady => ReadyToken != null;
        public bool IsComplete { get; private set; }
        public bool IsPaused => _pausedAtMs >= 0;
        public int FrameCount => _durations.Length;
        public int FrameIndex => _index;

        /// <summary>The clip could not be read or decoded. UI thread.</summary>
        public event Action<EmoteClipPlayer, string>? Failed;
        /// <summary>The single pass ended. UI thread, once per Load.</summary>
        public event Action<EmoteClipPlayer>? Completed;

        /// <summary>Monotonic ms clock (tests swap it).</summary>
        public Func<long> Now { get; set; } = static () => Environment.TickCount64;

        /// <summary>Drive the clock by hand (tests): no timer runs.</summary>
        internal bool ManualClock { get; set; }

        /// <summary>Start loading <paramref name="source"/>. The old frame stays on the Image until the new
        /// first frame replaces it.</summary>
        public Task Load(Uri source)
        {
            int gen = ++_generation;
            Source = source;
            ReadyToken = null;
            IsComplete = false;
            _pausedAtMs = -1;
            _timer.Stop();
            ReleaseCodec();
            return LoadAsync(gen, source);
        }

        private async Task LoadAsync(int gen, Uri source)
        {
            Decoded? d = null;
            string? error = null;
            try
            {
                var bytes = ReadBytes(source);   // pack resources resolve on any thread
                d = await Task.Run(() => Decode(bytes)).ConfigureAwait(true);
                if (d == null) error = "not decodable";
            }
            catch (Exception ex) { error = ex.Message; }

            if (gen != _generation) { d?.Dispose(); return; }   // superseded or stopped
            if (d == null) { Failed?.Invoke(this, error ?? "unknown"); return; }
            Adopt(d);
        }

        /// <summary>The clip's bytes from a pack:// resource or an absolute file; null when absent.</summary>
        internal static byte[]? ReadBytes(Uri source)
        {
            if (source.IsFile) return File.Exists(source.LocalPath) ? File.ReadAllBytes(source.LocalPath) : null;
            var sri = Application.GetResourceStream(source);
            if (sri == null) return null;
            using var s = sri.Stream;
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            return ms.ToArray();
        }

        internal sealed class Decoded : IDisposable
        {
            public SKCodec Codec = null!;
            public SKData Data = null!;
            public SKBitmap Canvas = null!;
            public int[] Durations = null!;
            public int[] Required = null!;
            public void Dispose() { Codec.Dispose(); Data.Dispose(); Canvas.Dispose(); }
        }

        /// <summary>Opens the clip and decodes frame 0 (any thread). Null when it is not an image.</summary>
        internal static Decoded? Decode(byte[]? bytes)
        {
            if (bytes == null || bytes.Length == 0) return null;
            var data = SKData.CreateCopy(bytes);
            var codec = SKCodec.Create(data);
            if (codec == null) { data.Dispose(); return null; }
            var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            var canvas = new SKBitmap(info);
            var frames = codec.FrameInfo;
            int count = Math.Max(1, codec.FrameCount);
            var durations = new int[count];
            var required = new int[count];
            for (int i = 0; i < count; i++)
            {
                int ms = i < frames.Length ? frames[i].Duration : 0;
                durations[i] = ms <= 10 ? DefaultFrameMs : Math.Max(MinFrameMs, ms);
                required[i] = i < frames.Length ? frames[i].RequiredFrame : -1;
            }
            canvas.Erase(SKColors.Transparent);
            var r = codec.GetPixels(info, canvas.GetPixels(), new SKCodecOptions(0));
            if (r != SKCodecResult.Success && r != SKCodecResult.IncompleteInput)
            {
                canvas.Dispose(); codec.Dispose(); data.Dispose();
                return null;
            }
            return new Decoded { Codec = codec, Data = data, Canvas = canvas, Durations = durations, Required = required };
        }

        /// <summary>Tests: take an already decoded clip synchronously (no file, no thread hop).</summary>
        internal void LoadDecoded(Uri source, Decoded d)
        {
            ++_generation;
            Source = source;
            ReadyToken = null;
            IsComplete = false;
            _pausedAtMs = -1;
            _timer.Stop();
            ReleaseCodec();
            Adopt(d);
        }

        private void Adopt(Decoded d)
        {
            _codec = d.Codec;
            _data = d.Data;
            _info = d.Canvas.Info;
            _durations = d.Durations;
            _required = d.Required;
            _index = 0;
            _bufferFrame = 0;
            _frameStartMs = Now();
            var bmp = EnsureBitmap();
            bmp.Lock();
            try
            {
                using var pm = d.Canvas.PeekPixels();
                pm.ReadPixels(_info, bmp.BackBuffer, bmp.BackBufferStride);
                bmp.AddDirtyRect(new Int32Rect(0, 0, _info.Width, _info.Height));
            }
            finally { bmp.Unlock(); }
            d.Canvas.Dispose();   // later frames decode straight into the bitmap
            Show();
            ReadyToken = new object();
            IsComplete = false;
            if (!ManualClock) _timer.Start();
        }

        /// <summary>Advance to the frame <paramref name="nowMs"/> falls on; raise Completed after the last
        /// frame's duration. Cheap when nothing is due.</summary>
        public void Tick(long nowMs)
        {
            if (!IsReady || IsComplete || IsPaused || _codec == null) return;
            bool moved = false;
            while (nowMs - _frameStartMs >= _durations[_index])
            {
                if (_index >= _durations.Length - 1 || !Step()) { Finish(moved); return; }
                moved = true;
            }
            if (moved) Show();

            bool Step()
            {
                _frameStartMs += _durations[_index];
                _index++;
                return DecodeInto(_index);
            }
        }

        private void Finish(bool moved)
        {
            IsComplete = true;
            _timer.Stop();
            if (moved) Show();
            Completed?.Invoke(this);
        }

        // The canvas holds _bufferFrame. A frame that builds on exactly that frame reuses it; any other
        // dependent frame lets the codec rebuild its required frame itself (priorFrame -1).
        private bool DecodeInto(int frame)
        {
            if (_codec == null || _bitmap == null) return false;
            int req = frame < _required.Length ? _required[frame] : -1;
            int prior = req >= 0 && req == _bufferFrame ? req : -1;
            _bitmap.Lock();
            try
            {
                if (req < 0)
                    using (var pm = new SKPixmap(_info, _bitmap.BackBuffer, _bitmap.BackBufferStride)) pm.Erase(SKColors.Transparent);
                var r = _codec.GetPixels(_info, _bitmap.BackBuffer, _bitmap.BackBufferStride, new SKCodecOptions(frame, prior));
                _bitmap.AddDirtyRect(new Int32Rect(0, 0, _info.Width, _info.Height));
                _bufferFrame = frame;
                return r == SKCodecResult.Success || r == SKCodecResult.IncompleteInput;
            }
            finally { _bitmap.Unlock(); }
        }

        private WriteableBitmap EnsureBitmap()
        {
            if (_bitmap == null || _bitmap.PixelWidth != _info.Width || _bitmap.PixelHeight != _info.Height)
                _bitmap = new WriteableBitmap(_info.Width, _info.Height, 96, 96, PixelFormats.Pbgra32, null);
            return _bitmap;
        }

        private void Show()
        {
            if (_bitmap != null && !ReferenceEquals(_target.Source, _bitmap)) _target.Source = _bitmap;
        }

        public void Pause()
        {
            if (IsPaused) return;
            _pausedAtMs = Now();
            _timer.Stop();
        }

        public void Play()
        {
            if (!IsPaused) return;
            _frameStartMs += Now() - _pausedAtMs;   // the frame on screen keeps the time it had left
            _pausedAtMs = -1;
            if (IsReady && !IsComplete && !ManualClock) _timer.Start();
        }

        /// <summary>Stop and free the clip; the Image is emptied when it still shows this player's frame.
        /// A load in flight is dropped.</summary>
        public void Stop()
        {
            _generation++;
            _timer.Stop();
            Source = null;
            ReadyToken = null;
            IsComplete = false;
            _pausedAtMs = -1;
            ReleaseCodec();
            if (_bitmap != null && ReferenceEquals(_target.Source, _bitmap)) _target.Source = null;
            _bitmap = null;
        }

        private void ReleaseCodec()
        {
            _codec?.Dispose(); _codec = null;
            _data?.Dispose(); _data = null;
            _durations = Array.Empty<int>();
            _required = Array.Empty<int>();
        }

        public void Dispose() => Stop();
    }
}
