// WPF EmoteClipPlayer.cs at 9654cbdc3:149-299, SKCodec clock, composition and stop.
using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Platform;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using SkiaSharp;
namespace ConditioningControlPanel.Avalonia.Views.AvatarTube
{
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
        private long _frameStartMs;
        private WriteableBitmap? _bitmap;
        public EmoteClipPlayer(Image target)
        {
            _target = target ?? throw new ArgumentNullException(nameof(target));
            _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(15) };
            _timer.Tick += (_, _) => Tick(Now());
        }
        public bool IsReady { get; private set; }
        public bool IsComplete { get; private set; }
        public int FrameCount => _durations.Length;
        public int FrameIndex => _index;
        public event Action<EmoteClipPlayer, string>? Failed;
        public event Action<EmoteClipPlayer>? Completed;
        public Func<long> Now { get; set; } = static () => Environment.TickCount64;
        internal bool ManualClock { get; set; }
        public async Task Load(Uri source)
        {
            int gen = ++_generation;
            IsReady = false;
            IsComplete = false;
            _timer.Stop();
            ReleaseCodec();
            Decoded? d = null;
            string? error = null;
            try
            {
                using var stream = source.IsFile ? File.OpenRead(source.LocalPath) : AssetLoader.Open(source);
                using var bytes = new MemoryStream(); stream.CopyTo(bytes);
                d = await Task.Run(() => Decode(bytes.ToArray()));
                if (d == null) error = "not decodable";
            }
            catch (Exception ex) { error = ex.Message; }
            if (gen != _generation) { d?.Dispose(); return; }
            if (d == null) { Failed?.Invoke(this, error ?? "unknown"); return; }
            Adopt(d);
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
        internal void LoadDecoded(Decoded d) { Stop(); Adopt(d); }
        private void Adopt(Decoded d)
        {
            _codec = d.Codec;
            _data = d.Data;
            _info = d.Canvas.Info;
            _durations = d.Durations;
            _required = d.Required;
            _index = 0;
            _frameStartMs = Now();
            var bmp = EnsureBitmap();
            using (var fb = bmp.Lock())
            using (var pm = d.Canvas.PeekPixels())
                pm.ReadPixels(_info, fb.Address, fb.RowBytes);
            d.Canvas.Dispose();
            Show();
            IsReady = true;
            IsComplete = false;
            if (!ManualClock) _timer.Start();
        }
        public void Tick(long nowMs)
        {
            if (!IsReady || IsComplete || _codec == null) return;
            bool moved = false;
            while (nowMs - _frameStartMs >= _durations[_index])
            {
                if (_index >= _durations.Length - 1) { Finish(moved); return; }
                _frameStartMs += _durations[_index++];
                if (!DecodeInto(_index)) { Finish(moved); return; }
                moved = true;
            }
            if (moved) Show();
        }
        private void Finish(bool moved)
        {
            IsComplete = true;
            _timer.Stop();
            if (moved) Show();
            Completed?.Invoke(this);
        }
        private bool DecodeInto(int frame)
        {
            if (_codec == null || _bitmap == null) return false;
            int req = frame < _required.Length ? _required[frame] : -1;
            int prior = req >= 0 && req == frame - 1 ? req : -1;
            using var fb = _bitmap.Lock();
            if (req < 0)
                using (var pm = new SKPixmap(_info, fb.Address, fb.RowBytes)) pm.Erase(SKColors.Transparent);
            var result = _codec.GetPixels(_info, fb.Address, fb.RowBytes, new SKCodecOptions(frame, prior));
            return result == SKCodecResult.Success || result == SKCodecResult.IncompleteInput;
        }
        private WriteableBitmap EnsureBitmap()
        {
            if (_bitmap == null || _bitmap.PixelSize.Width != _info.Width || _bitmap.PixelSize.Height != _info.Height)
            {
                _bitmap?.Dispose();
                _bitmap = new WriteableBitmap(new PixelSize(_info.Width, _info.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
            }
            return _bitmap;
        }
        private void Show()
        {
            if (_bitmap != null) _target.Source = _bitmap;
            _target.InvalidateVisual();
        }
        public void Stop()
        {
            _generation++;
            _timer.Stop();
            IsReady = false;
            IsComplete = false;
            ReleaseCodec();
            if (_bitmap != null && ReferenceEquals(_target.Source, _bitmap)) _target.Source = null;
            _bitmap?.Dispose();
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
