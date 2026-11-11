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
    private bool _rainGlass;

    partial void OnReadyCore()
    {
        _rainIdle = NextRainIdle();
        _glassCanvas.ClipToBounds = true;
        PointerActivity += (_, _) => _rainActivity = DateTime.UtcNow;
        Moved += (_, _) => { CloseRainGlass(); _rainActivity = DateTime.UtcNow; };
        _rainWatch = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _rainWatch.Tick += RainTick;
        _rainWatch.Start();
        Closed += (_, _) => { _rainWatch.Stop(); TearDownRain(); };
    }

    private TimeSpan NextRainIdle() => EmiDebug.IdleMs is int ms
        ? TimeSpan.FromMilliseconds(ms) : TimeSpan.FromSeconds(30 + _rainRandom.NextDouble() * 30);

    private void RainTick(object? sender, EventArgs e)
    {
        if (!IsVisible) { _rainActivity = DateTime.UtcNow; return; }
        if (_rainGlass)
        {
            var elapsed = (DateTime.UtcNow - _rainOpened).TotalMilliseconds;
            if (elapsed >= 10000) { CloseRainGlass(); return; }
            double height = _glassCanvas.Height;
            for (int i = 0; i < _rainDrops.Count; i++)
                Canvas.SetTop(_rainDrops[i], ((i * 37 + elapsed * (0.035 + (i % 4) * 0.012)) % (height + 40)) - 20);
            return;
        }
        if (DateTime.UtcNow - _rainActivity < _rainIdle) return;
        _rainActivity = DateTime.UtcNow;
        _rainIdle = NextRainIdle();
        // Other glass channels are still absent: skip their turns rather than filling the glass with rain.
        if (_rainRandom.Next(10) == 0) TryShowRainChannel();
    }

    internal bool TryShowRainChannel()
    {
        if (!IsVisible || PresentationActive || InputLocked || Transiting || ChainLive || RingOpen ||
            !CoreSettings.Current.EmiDeskGlass || EmiLineEngine.BedtimeSet || EmiLineEngine.Instance.HoldActive ||
            CoreEngine.Video?.IsPlaying == true || _rainGlass) return false;
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
            _rainOpened = DateTime.UtcNow;
            _rainGlass = true;
            RainTick(null, EventArgs.Empty);
            return true;
        }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] rain channel failed"); CloseRainGlass(); return false; }
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
        EmiGifRain.Stop();
    }
}
