// WPF Controls/Leash/LeashSnapCard.cs:31-144 and LeashLook.cs:226-275.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Leash;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Controls;
using F = ConditioningControlPanel.Avalonia.Views.Controls.FriendsDrawer;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Leash;

internal sealed class LeashSnapCard : Border
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    internal static Func<string, Task<byte[]>> FetchPhoto = url => Http.GetByteArrayAsync(url);
    private readonly Canvas _sparks = new() { IsHitTestVisible = false, ClipToBounds = false, Tag = "leash-snap-sparks" };
    private readonly ScaleTransform _chainScale = new(1, 1), _impactScale = new(1, 1), _titleScale = new(1, 1), _cardScale = new(1, 1);
    private readonly RotateTransform _tagRotation = new();
    private readonly TextBlock _title;
    private readonly Control _right;
    private readonly List<(Ellipse Dot, double X, double Y, double Duration)> _particles = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Stopwatch _clock = new();
    private readonly double _amount;
    private readonly bool _swing;
    private bool _burst;
    internal event Action? Closed;

    internal LeashSnapCard(LeashPerson holder, LeashPerson leashed, MotionLevel motion)
    {
        _amount = motion == MotionLevel.Off ? 0 : motion == MotionLevel.Reduced ? .5 : 1;
        _swing = motion == MotionLevel.Full && AmbientFxCanvas.Env.AllowAmbientLoops;
        Width = 380; Padding = new Thickness(18); Background = F.Foot; BorderBrush = F.Gold;
        BorderThickness = new Thickness(1); CornerRadius = new CornerRadius(18); Tag = "leash-snap"; RenderTransform = _cardScale;
        var root = new Grid(); var body = new StackPanel(); root.Children.Add(body); root.Children.Add(_sparks); Child = root;
        var row = new Grid { Height = 84, ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        row.Children.Add(Avatar(holder));
        var chain = Chain(); chain.RenderTransformOrigin = new RelativePoint(0, .5, RelativeUnit.Relative); chain.RenderTransform = _chainScale;
        var chainHost = new Border { ClipToBounds = true, Height = 18, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0), Child = chain };
        Grid.SetColumn(chainHost, 1); row.Children.Add(chainHost);
        var rightHost = new Grid(); _right = Avatar(leashed); _right.RenderTransform = _impactScale;
        _right.RenderTransformOrigin = RelativePoint.Center; rightHost.Children.Add(_right);
        var tag = HeartTag(); tag.RenderTransform = _tagRotation; tag.RenderTransformOrigin = new RelativePoint(.5, 0, RelativeUnit.Relative);
        tag.HorizontalAlignment = HorizontalAlignment.Left; tag.VerticalAlignment = VerticalAlignment.Bottom;
        tag.Margin = new Thickness(-4, 0, 0, -10); rightHost.Children.Add(tag);
        Grid.SetColumn(rightHost, 2); row.Children.Add(rightHost); body.Children.Add(row);
        _title = F.Label(Loc.Get("leash_snap_title"), 26, F.Gold, F.Display, FontWeight.SemiBold);
        _title.Tag = "leash-snap-title"; _title.HorizontalAlignment = HorizontalAlignment.Center;
        _title.Margin = new Thickness(0, 10, 0, 0); _title.RenderTransform = _titleScale; _title.RenderTransformOrigin = RelativePoint.Center;
        body.Children.Add(_title);
        var panels = new UniformGrid { Columns = 2, Margin = new Thickness(0, 12, 0, 0) };
        panels.Children.Add(Recap(Loc.GetF("leash_snap_holder", holder.Name), "leash-snap-holder", F.Gold));
        panels.Children.Add(Recap(Loc.GetF("leash_snap_leashed", leashed.Name), "leash-snap-leashed", F.Mint));
        body.Children.Add(panels);
        var ok = LeashCardUi.Action(Loc.Get("leash_snap_ok"), "leash-snap-ok", () => { Closed?.Invoke(); return Task.CompletedTask; });
        ok.Margin = new Thickness(0, 16, 0, 0); body.Children.Add(ok);
        _timer.Tick += (_, _) => Advance(_clock.Elapsed.TotalMilliseconds);
        DetachedFromVisualTree += (_, _) => Stop();
    }

    private static Control Recap(string text, string tag, IBrush ink) => new Border
    {
        Background = F.Raised, BorderBrush = ink, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10),
        Padding = new Thickness(10), Margin = new Thickness(3), Child = LeashCardUi.Text(text, tag, ink),
    };
    private static Canvas Chain()
    {
        var canvas = new Canvas { Width = 190, Height = 14, Tag = "leash-chain" };
        var light = new SolidColorBrush(Color.FromRgb(0xD9, 0xC6, 0xFF));
        var dark = new SolidColorBrush(Color.FromRgb(0x9F, 0x86, 0xD0));
        for (int i = 0; i < 18; i++)
        {
            bool face = i % 2 == 0;
            var link = new Rectangle { Width = 15, Height = face ? 14 : 5.88, RadiusX = face ? 7 : 2, RadiusY = face ? 7 : 2,
                Stroke = face ? light : dark, StrokeThickness = face ? 2.2 : 0, Fill = face ? Brushes.Transparent : dark };
            Canvas.SetLeft(link, i * 11 - 2); Canvas.SetTop(link, face ? 0 : 4.06); canvas.Children.Add(link);
        }
        return canvas;
    }
    private static Viewbox HeartTag()
    {
        var c = new Canvas { Width = 46, Height = 52 };
        c.Children.Add(new global::Avalonia.Controls.Shapes.Path { Data = Geometry.Parse("M23,0 V8 M19.8,10 A3.2,3.2 0 1 0 26.2,10 A3.2,3.2 0 1 0 19.8,10 Z"), Stroke = F.Lilac, StrokeThickness = 2 });
        c.Children.Add(new global::Avalonia.Controls.Shapes.Path { Data = Geometry.Parse("M23,50 C10,41 4,34 4,25.5 C4,19 8.6,14.5 14.4,14.5 C18,14.5 21.2,16.3 23,19.1 C24.8,16.3 28,14.5 31.6,14.5 C37.4,14.5 42,19 42,25.5 C42,34 36,41 23,50 Z"), Fill = F.Gold });
        return new Viewbox { Height = 26, Child = c, Tag = "leash-tag" };
    }
    private static Control Avatar(LeashPerson person)
    {
        var host = (Grid)F.Avatar(person.Name, 68, null, person.Look);
        host.Tag = "leash-snap-avatar:" + person.Id;
        if (!string.IsNullOrEmpty(person.AvatarUrl)) _ = PaintPhoto(host, person.AvatarUrl);
        return host;
    }
    // WPF FriendsLook.cs:223-254: first-party relative paths or HTTPS; failure keeps initials.
    internal static async Task PaintPhoto(Grid host, string url)
    {
        try
        {
            var full = url.StartsWith("/", StringComparison.Ordinal) ? "https://codebambi-proxy.vercel.app" + url : url;
            if (!full.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return;
            var bytes = await FetchPhoto(full);
            using var stream = new MemoryStream(bytes);
            var image = Bitmap.DecodeToWidth(stream, 96);
            ((Border)host.Children[0]).Background = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
            ((Border)host.Children[0]).Child = null;
        }
        catch { }
    }
    internal void Play()
    {
        LeashSound.Snap();
        if (_amount == 0) return;
        _title.Opacity = 0; Advance(0); _clock.Restart(); _timer.Start();
    }
    internal void Stop() { _timer.Stop(); _clock.Stop(); }
    // Testable clock step; same 340 ms house curve and 300 ms title delay as WPF.
    internal void Advance(double ms)
    {
        if (_amount == 0) return;
        _chainScale.ScaleX = Ease(Math.Clamp(ms / 340, 0, 1));
        _cardScale.ScaleX = _cardScale.ScaleY = 1 + .08 * _amount * (1 - Ease(Math.Clamp(ms / 340, 0, 1))); // WPF LeashSurfaces.cs:308-311 Thud 1.08
        if (ms >= 300) { _title.Opacity = 1; _titleScale.ScaleX = _titleScale.ScaleY = 1 + .5 * _amount * (1 - Ease(Math.Clamp((ms - 300) / 340, 0, 1))); }
        if (ms >= 340 && !_burst) Burst();
        if (_burst)
        {
            _impactScale.ScaleX = _impactScale.ScaleY = 1 + .18 * _amount * (1 - Ease(Math.Clamp((ms - 340) / 340, 0, 1)));
            for (int i = _particles.Count - 1; i >= 0; i--)
            {
                var (dot, x, y, duration) = _particles[i];
                var p = Math.Clamp((ms - 340) / duration, 0, 1); var q = 1 - (1 - p) * (1 - p);
                dot.RenderTransform = new TranslateTransform(x * q, (y + 10) * q); dot.Opacity = 1 - p;
                if (p >= 1) { _sparks.Children.Remove(dot); _particles.RemoveAt(i); }
            }
        }
        if (_swing) _tagRotation.Angle = -8 * Math.Cos(ms * Math.PI / 1500);
        if (ms >= 1200) { if (_swing) _timer.Interval = TimeSpan.FromMilliseconds(42); else { _timer.Stop(); _clock.Stop(); } } // WPF swing 24 fps
    }
    private void Burst()
    {
        _burst = true;
        var centre = _right.TranslatePoint(new Point(_right.Bounds.Width / 2, _right.Bounds.Height / 2), _sparks) ?? new Point(300, 42);
        var rng = new Random();
        for (int i = 0; i < (int)(22 * _amount); i++)
        {
            double size = 3 + rng.NextDouble() * 4, angle = rng.NextDouble() * Math.PI * 2, distance = (24 + rng.NextDouble() * 60) * _amount;
            var dot = new Ellipse { Width = size, Height = size, Fill = i % 3 == 2 ? F.Pink : F.Gold, Tag = "leash-spark" };
            Canvas.SetLeft(dot, centre.X - size / 2); Canvas.SetTop(dot, centre.Y - size / 2); _sparks.Children.Add(dot);
            _particles.Add((dot, Math.Cos(angle) * distance, Math.Sin(angle) * distance, 520 + rng.Next(320)));
        }
    }
    private static double Ease(double x)
    {
        double t = x;
        static double Bez(double p, double a, double b) => 3 * a * p * (1 - p) * (1 - p) + 3 * b * p * p * (1 - p) + p * p * p;
        for (int i = 0; i < 8; i++)
        {
            double d = .6 * (1 - t) * (1 - t) + 1.2 * t * (1 - t) + 1.8 * t * t;
            if (Math.Abs(d) < 1e-6) break;
            t = Math.Clamp(t - (Bez(t, .2, .4) - x) / d, 0, 1);
        }
        return Bez(t, 1.5, 1);
    }
}
