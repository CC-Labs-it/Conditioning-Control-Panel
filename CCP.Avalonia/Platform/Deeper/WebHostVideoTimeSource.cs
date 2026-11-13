using System;
using System.Globalization;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Services.Deeper;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform.Deeper;

// WPF Services/Deeper/BrowserVideoTimeSource.cs:25: poll the first page video at 10 Hz.
internal sealed class WebHostVideoTimeSource : IPlaybackTimeSource, IDisposable
{
    private readonly WebHost _host;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private bool _attached, _polling;
    private int _generation;
    private double _time, _duration, _lastTime = -1, _lastDuration = -1;
    private double _x, _y, _width = 1, _height = 1;
    private bool _playing;
    private const string PollScript = "(function(){var v=document.querySelector('video');if(!v)return null;var r=v.getBoundingClientRect(),w=window.innerWidth||1,h=window.innerHeight||1;return JSON.stringify({t:v.currentTime||0,d:isFinite(v.duration)?v.duration:0,paused:!!v.paused,rx:r.left/w,ry:r.top/h,rw:r.width/w,rh:r.height/h});})();";

    public WebHostVideoTimeSource(WebHost host)
    {
        _host = host;
        _timer.Tick += async (_, _) => await PollAsync();
    }

    public event Action<double>? PlaybackTimeChanged;
    public void Attach() { if (_attached) return; _attached = true; _generation++; _timer.Start(); }
    public void Detach() { _attached = false; _generation++; _timer.Stop(); }
    public void Dispose() => Detach();

    internal async Task PollAsync()
    {
        if (!_attached || _polling) return;
        _polling = true;
        int generation = _generation;
        try
        {
            var json = await _host.InvokeScriptAsync(PollScript);
            if (!_attached || generation != _generation || string.IsNullOrWhiteSpace(json) || json == "null") return;
            using var first = JsonDocument.Parse(json);
            // WebView2 returns a JSON string literal; WebKitGTK returns the string itself.
            string data = first.RootElement.ValueKind == JsonValueKind.String ? first.RootElement.GetString()! : json;
            using var document = JsonDocument.Parse(data);
            var obj = document.RootElement;
            _time = obj.GetProperty("t").GetDouble();
            _duration = obj.GetProperty("d").GetDouble();
            _playing = !obj.GetProperty("paused").GetBoolean();
            _x = obj.GetProperty("rx").GetDouble(); _y = obj.GetProperty("ry").GetDouble();
            _width = obj.GetProperty("rw").GetDouble(); _height = obj.GetProperty("rh").GetDouble();
            if (Math.Abs(_time - _lastTime) > 0.001 || Math.Abs(_duration - _lastDuration) > 0.001)
            {
                _lastTime = _time; _lastDuration = _duration;
                PlaybackTimeChanged?.Invoke(_time);
            }
        }
        catch (Exception ex) { Log.Debug("Browser video poll: {Error}", ex.Message); }
        finally { _polling = false; }
    }

    public double GetCurrentTimeSeconds() => _time;
    public double GetDurationSeconds() => _duration;
    public bool IsPlaying => _playing;
    public void Seek(double seconds) => Send(string.Format(CultureInfo.InvariantCulture,
        "(function(){{var v=document.querySelector('video');if(v)v.currentTime={0:0.###};}})();", Math.Max(0, seconds)));
    public void Pause() => Send("(function(){var v=document.querySelector('video');if(v)v.pause();})();");
    public void Play() => Send("(function(){var v=document.querySelector('video');if(v)v.play();})();");
    private void Send(string script) { _ = _host.InvokeScriptAsync(script); }
    public PlaybackRect GetVideoRect()
    {
        if (TopLevel.GetTopLevel(_host) is not { } top || _host.Bounds.Width <= 0 || _host.Bounds.Height <= 0)
            return PlaybackRect.Empty;
        try
        {
            var origin = _host.PointToScreen(new Point(0, 0));
            double scale = top.RenderScaling;
            return new PlaybackRect(origin.X / scale + _x * _host.Bounds.Width, origin.Y / scale + _y * _host.Bounds.Height,
                _width * _host.Bounds.Width, _height * _host.Bounds.Height);
        }
        catch (InvalidOperationException) { return PlaybackRect.Empty; }
    }
}
