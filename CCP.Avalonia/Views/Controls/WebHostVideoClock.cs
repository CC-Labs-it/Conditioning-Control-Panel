using System;
using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Services.Deeper;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Avalonia.Views.Controls
{
    /// <summary>Playback source for the first video on a WebHost page. WPF: BrowserVideoTimeSource.cs:25.</summary>
    internal sealed class WebHostVideoClock : IPlaybackTimeSource, IDisposable
    {
        private readonly WebHost _web;
        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
        private readonly string _id = Guid.NewGuid().ToString("N");
        private readonly Stopwatch _elapsed = new();
        private bool _attached;
        private double _time, _duration, _x, _y, _width, _height;
        private bool _playing, _hasVideo;

        public event Action<double>? PlaybackTimeChanged;
        public bool IsPlaying => _playing;
        public double GetDurationSeconds() => _duration;
        public double GetCurrentTimeSeconds()
        {
            var time = _time + (_playing ? _elapsed.Elapsed.TotalSeconds : 0);
            return _duration > 0 ? Math.Min(time, _duration) : time;
        }

        public WebHostVideoClock(WebHost web)
        {
            _web = web ?? throw new ArgumentNullException(nameof(web));
            _timer.Tick += OnTick;
        }

        public void Attach()
        {
            if (_attached) return;
            _attached = true;
            _web.WebMessage += OnMessage;
            _timer.Start();
            Poll();
        }

        public void Detach()
        {
            if (!_attached) return;
            _attached = false;
            _timer.Stop();
            _web.WebMessage -= OnMessage;
            _elapsed.Reset();
            _time = _duration = 0;
            _playing = _hasVideo = false;
        }

        private void OnTick(object? sender, EventArgs e)
        {
            Poll();
            if (_hasVideo && _playing) PlaybackTimeChanged?.Invoke(GetCurrentTimeSeconds());
        }

        internal void Poll()
        {
            if (!_attached) return;
            // A message avoids adapter differences in InvokeScript return encoding.
            _ = _web.InvokeScriptAsync(@"(function(){var v=document.querySelector('video'),r=v&&v.getBoundingClientRect();
                var m={ccpVideoClock:'" + _id + @"',hasVideo:!!v,t:v&&isFinite(v.currentTime)?v.currentTime:0,
                d:v&&isFinite(v.duration)?v.duration:0,playing:!!v&&!v.paused&&!v.ended,
                x:r?r.left/(innerWidth||1):0,y:r?r.top/(innerHeight||1):0,
                w:r?r.width/(innerWidth||1):0,h:r?r.height/(innerHeight||1):0};
                if(window.invokeCSharpAction)window.invokeCSharpAction(JSON.stringify(m));
                else if(window.chrome&&window.chrome.webview)window.chrome.webview.postMessage(m);})();");
        }

        private void OnMessage(string message)
        {
            try
            {
                var data = JObject.Parse(message);
                if ((string?)data["ccpVideoClock"] != _id) return;
                var oldTime = GetCurrentTimeSeconds();
                var oldDuration = _duration;
                var oldPlaying = _playing;
                _hasVideo = (bool?)data["hasVideo"] == true;
                _time = _hasVideo ? NonNegative((double?)data["t"] ?? 0) : 0;
                _duration = _hasVideo ? NonNegative((double?)data["d"] ?? 0) : 0;
                _playing = _hasVideo && (bool?)data["playing"] == true;
                _x = (double?)data["x"] ?? 0; _y = (double?)data["y"] ?? 0;
                _width = NonNegative((double?)data["w"] ?? 0); _height = NonNegative((double?)data["h"] ?? 0);
                _elapsed.Restart();
                if (Math.Abs(oldTime - _time) > 0.001 || oldDuration != _duration || oldPlaying != _playing)
                    PlaybackTimeChanged?.Invoke(_time);
            }
            catch (Exception ex) when (ex is Newtonsoft.Json.JsonException || ex is FormatException || ex is InvalidCastException || ex is OverflowException) { }
        }

        private static double NonNegative(double value) => double.IsFinite(value) ? Math.Max(0, value) : 0;
        public void Seek(double seconds) => Send("v.currentTime=" + NonNegative(seconds).ToString("R", CultureInfo.InvariantCulture));
        public void Pause() => Send("v.pause()");
        public void Play() => Send("v.play()");
        private void Send(string action) => _ = _web.InvokeScriptAsync("(function(){var v=document.querySelector('video');if(v){" + action + ";}})();");

        public PlaybackRect GetVideoRect()
        {
            if (!_hasVideo || _web.Bounds.Width <= 0 || _web.Bounds.Height <= 0 || TopLevel.GetTopLevel(_web) is not { } top)
                return PlaybackRect.Empty;
            try
            {
                var origin = _web.PointToScreen(new Point(0, 0));
                var scale = top.RenderScaling;
                return new PlaybackRect(origin.X / scale + _x * _web.Bounds.Width, origin.Y / scale + _y * _web.Bounds.Height,
                    _width * _web.Bounds.Width, _height * _web.Bounds.Height);
            }
            catch (InvalidOperationException) { return PlaybackRect.Empty; }
        }

        public void Dispose() => Detach();
    }
}
