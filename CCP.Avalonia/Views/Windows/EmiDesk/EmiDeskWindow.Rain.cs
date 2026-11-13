using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ConditioningControlPanel.Services.EmiDesk;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;

public partial class EmiDeskWindow
{
    private readonly Random _rainRandom = new();
    private readonly List<Bitmap> _rainBitmaps = new();
    private readonly List<Image> _rainDrops = new();
    private DispatcherTimer? _rainWatch;
    private DateTime _rainActivity = DateTime.UtcNow;
    private DateTime _rainOpened;
    private TimeSpan _rainIdle = TimeSpan.FromSeconds(30);
    private bool _rainGlass, _rainGlassClick;
    private Canvas? _rainGlitch;
    private int _rainGlitchFrame;
    private bool _rainOfferSent;

    partial void OnReadyCore()
    {
        _rainIdle = NextRainIdle();
        _glassCanvas.ClipToBounds = true;
        PointerActivity += (_, _) => { if (!_rainGlassClick) CloseRainGlass(); _rainActivity = DateTime.UtcNow; };
        Moved += (_, _) => { CloseRainGlass(); _rainActivity = DateTime.UtcNow; };
        // Idle watch at a slow beat; the 55 ms rain steps only while the glass rain is live.
        _rainWatch = new DispatcherTimer { Interval = RainIdleBeat };
        _rainWatch.Tick += RainTick;
        _rainWatch.Start();
        Closed += (_, _) => { _rainWatch.Stop(); TearDownRain(); };
    }

    private static readonly TimeSpan RainIdleBeat = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan RainLiveBeat = TimeSpan.FromMilliseconds(55);

    private TimeSpan NextRainIdle() => EmiDebug.IdleMs is int ms
        ? TimeSpan.FromMilliseconds(ms) : TimeSpan.FromSeconds(30 + _rainRandom.NextDouble() * 30);

    private void RainTick(object? sender, EventArgs e)
    {
        if (RainAskLive && (PresentationActive || CoreSession.IsSessionRunning || CoreEngine.Video?.IsPlaying == true ||
            ConditioningControlPanel.Services.Safety.PanicAftermath.TriggersSilenced())) CancelRainOffer();
        if (!IsVisible) { _rainActivity = DateTime.UtcNow; return; }
        if (_rainGlass)
        {
            var elapsed = (DateTime.UtcNow - _rainOpened).TotalMilliseconds;
            if (_rainGlitch != null)
            {
                if (elapsed < 220) { int frame = (int)(elapsed / 55); if (frame != _rainGlitchFrame) { _rainGlitchFrame = frame; PaintRainGlitch(); } return; }
                _glassCanvas.Children.Remove(_rainGlitch); _rainGlitch = null; _rainOpened = DateTime.UtcNow;
                if (!_rainOfferSent) { _rainOfferSent = true; FireRainOffer(); }
                return;
            }
            if (elapsed >= 10000) { CloseRainGlass(); return; }
            double height = _glassCanvas.Height;
            for (int i = 0; i < _rainDrops.Count; i++)
                Canvas.SetTop(_rainDrops[i], ((i * 37 + elapsed * (0.035 + (i % 4) * 0.012)) % (height + 40)) - 20);
            return;
        }
        if (DateTime.UtcNow - _rainActivity < _rainIdle) return;
        _rainActivity = DateTime.UtcNow;
        _rainIdle = NextRainIdle();
        TryShowRainChannel(); // rain is the only channel implemented on this head
    }

    internal bool TryShowRainChannel()
    {
        if (!IsVisible || PresentationActive || InputLocked || Transiting || ChainLive || RingOpen ||
            !CoreSettings.Current.EmiDeskGlass || EmiLineEngine.BedtimeSet || EmiLineEngine.Instance.HoldActive ||
            CoreEngine.Video?.IsPlaying == true || _rainGlass || RainAskLive) return false;
        try
        {
            var files = EmiGifRain.Images().OrderBy(_ => _rainRandom.Next()).Take(4);
            foreach (var file in files)
            {
                try { using var stream = File.OpenRead(file); _rainBitmaps.Add(Bitmap.DecodeToWidth(stream, 40)); }
                catch (Exception ex) { Log.Debug(ex, "[EmiDesk] rain thumbnail failed"); }
            }
            if (_rainBitmaps.Count == 0) return false;
            var width = _glassCanvas.Width;
            var height = _glassCanvas.Height;
            _glassCanvas.Children.Add(new Border { Width = width, Height = height, Background = Brush.Parse("#0E0E1C") });
            double size = Math.Min(20, width * 0.22);
            int count = Math.Max(5, Math.Min(12, (int)(width / size) * 2));
            for (int i = 0; i < count; i++)
            {
                var image = new Image { Source = _rainBitmaps[i % _rainBitmaps.Count], Width = size, Height = size,
                    Stretch = Stretch.UniformToFill, Opacity = 0.9 };
                Canvas.SetLeft(image, (i * 37) % Math.Max(1, width - size));
                _glassCanvas.Children.Add(image);
                _rainDrops.Add(image);
            }
            _rainOfferSent = false;
            // Gentle visuals: no glitch flicker; the rain shows at once and the offer follows.
            if (!ConditioningControlPanel.Services.Safety.GentleVisuals.On)
            {
                _rainGlitch = new Canvas { Width = width, Height = height, Background = Brush.Parse("#0E0E1C") };
                _glassCanvas.Children.Add(_rainGlitch);
                _rainGlitchFrame = 0; PaintRainGlitch();
            }
            _rainOpened = DateTime.UtcNow;
            _rainGlass = true;
            if (_rainWatch != null) _rainWatch.Interval = RainLiveBeat;
            if (_rainGlitch == null) { _rainOfferSent = true; FireRainOffer(); }
            RainTick(null, EventArgs.Empty);
            return true;
        }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] rain channel failed"); CloseRainGlass(); return false; }
    }

    private void PaintRainGlitch()
    {
        if (_rainGlitch == null) return;
        _rainGlitch.Children.Clear();
        for (int i = 0, n = 3 + _rainRandom.Next(3); i < n; i++)
        {
            double h = Math.Max(1, _rainGlitch.Height * (0.03 + _rainRandom.NextDouble() * 0.1));
            var band = new Border { Width = _rainGlitch.Width, Height = h, Background = Brush.Parse("#FF69B4"), Opacity = 0.18 + _rainRandom.NextDouble() * 0.5 };
            Canvas.SetLeft(band, (_rainRandom.NextDouble() - 0.5) * _rainGlitch.Width * 0.5);
            Canvas.SetTop(band, _rainRandom.NextDouble() * Math.Max(1, _rainGlitch.Height - h));
            _rainGlitch.Children.Add(band);
        }
    }

    partial void OnGlassLiveQuery(ref bool live) { if (_rainGlass) live = true; }
    partial void OnGlassClickedCore(ref bool handled)
    {
        if (!_rainGlass) return;
        handled = true;
        CloseRainGlass();
        EmiGifRain.Start(this);
    }

    private void CloseRainGlass()
    {
        _rainGlass = false;
        if (_rainWatch != null) _rainWatch.Interval = RainIdleBeat;
        _rainGlitch = null;
        _glassCanvas.Children.Clear();
        _rainDrops.Clear();
        foreach (var bitmap in _rainBitmaps) bitmap.Dispose();
        _rainBitmaps.Clear();
        _rainActivity = DateTime.UtcNow;
        _rainIdle = NextRainIdle();
    }

    private void TearDownRain()
    {
        CloseRainGlass();
        CancelRainOffer();
        EmiGifRain.Stop();
    }
}
