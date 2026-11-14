// WPF Controls/Leash/LeashSnapCard.cs:20-146 and LeashLook.cs:226-276 (chain, heart tag). LeashSurfaces.ShowAsk
// (:205-209) opens it on the leashed side's accept, OnEvent (:135) on the holder side's accepted answer.
// Flat on this head (P82): the card's DropShadowEffect is a BoxShadow, the sparks carry no glow. Motion runs on
// the window's shared beat (BeatLoop), never an own frame timer or an infinite Animation.
// Missing: the avatar photos (look and initials only).
using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Billboard;
using ConditioningControlPanel.Services.Leash;
using ConditioningControlPanel.Services.Safety;
using F = ConditioningControlPanel.Avalonia.Views.Controls.FriendsDrawer;
namespace ConditioningControlPanel.Avalonia.Views.Controls.Leash;
/// <summary>THE SNAP: the two avatars, the chain shooting across, "Leashed." and both recaps.</summary>
public sealed class LeashSnapCard : Border
{
    private readonly Canvas _sparks = new() { IsHitTestVisible = false, ClipToBounds = false, Tag = "leash-snap-sparks" };
    private readonly ScaleTransform _chainScale = new(1, 1), _impactScale = new(1, 1), _titleScale = new(1, 1);
    private readonly RotateTransform _tagTurn = new();
    private readonly TextBlock _title;
    private readonly Control _right;
    private readonly List<(Ellipse Dot, double X, double Y, double Ms)> _particles = new();
    private BeatLoop? _loop;
    private double _amount;
    private bool _swing, _burst;
    internal LeashPerson Holder { get; }
    internal LeashPerson Leashed { get; }
    internal bool MotionActive => _loop?.IsRunning == true;
    public event Action? Closed;
    /// <summary>--render-all (RenderProof): a sample snap.</summary>
    internal LeashSnapCard() : this(new LeashPerson("sample", "Juno", null), new LeashPerson("me", "You", null)) { }
    internal LeashSnapCard(LeashPerson holder, LeashPerson leashed)
    {
        (Holder, Leashed) = (holder, leashed);
        Width = 380;
        Background = LeashAskCard.Glass;
        BorderBrush = LeashAskCard.GoldDeep;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(18);
        Padding = new Thickness(18);
        BoxShadow = BoxShadows.Parse("0 10 34 0 #99000000");
        Tag = "leash-snap";
        var row = new Grid { Height = 84, ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        row.Children.Add(F.Avatar(holder.Name, 68, null, holder.Look));
        var chain = Chain();
        chain.RenderTransformOrigin = new RelativePoint(0, 0.5, RelativeUnit.Relative);
        chain.RenderTransform = _chainScale;
        var chainHost = new Border { ClipToBounds = true, Height = 18, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0), Child = chain };
        Grid.SetColumn(chainHost, 1); row.Children.Add(chainHost);
        _right = F.Avatar(leashed.Name, 68, null, leashed.Look);
        _right.RenderTransformOrigin = RelativePoint.Center;
        _right.RenderTransform = _impactScale;
        var tag = HeartTag(26);
        tag.RenderTransformOrigin = new RelativePoint(0.5, 0, RelativeUnit.Relative);
        tag.RenderTransform = _tagTurn;
        var rightHost = new Grid { Children = { _right, tag } };
        Grid.SetColumn(rightHost, 2); row.Children.Add(rightHost);
        _title = F.Label(Loc.Get("leash_snap_title"), 26, F.Gold, F.Display, FontWeight.SemiBold);
        _title.Tag = "leash-snap-title";
        _title.HorizontalAlignment = HorizontalAlignment.Center;
        _title.Margin = new Thickness(0, 10, 0, 0);
        _title.RenderTransformOrigin = RelativePoint.Center;
        _title.RenderTransform = _titleScale;
        var recap = new UniformGrid { Columns = 2, Margin = new Thickness(0, 12, 0, 0), Children = {
            Recap(Loc.GetF("leash_snap_holder", holder.Name), F.Gold, "leash-snap-holder"),
            Recap(Loc.GetF("leash_snap_leashed", leashed.Name), F.Mint, "leash-snap-leashed") } };
        var ok = F.Pill(new TextBlock { Text = Loc.Get("leash_snap_ok"), FontFamily = F.Display, FontSize = 15 }, F.Pink, F.Text, "leash-snap-ok", F.Pink);
        ok.HorizontalAlignment = HorizontalAlignment.Stretch;
        ok.HorizontalContentAlignment = HorizontalAlignment.Center;
        ok.Padding = new Thickness(12, 9);
        ok.Margin = new Thickness(0, 16, 0, 0);
        ok.Click += (_, _) => Closed?.Invoke();
        Child = new Grid { Children = { new StackPanel { Children = { row, _title, recap, ok } }, _sparks } };
        DetachedFromVisualTree += (_, _) => Stop();
    }
    private static Control Recap(string text, IBrush fg, string tag) => new Border
    {
        Background = F.Raised, CornerRadius = new CornerRadius(10), Padding = new Thickness(10), Margin = new Thickness(3), Tag = tag,
        Child = new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = fg, TextWrapping = TextWrapping.Wrap },
    };
    /// <summary>WPF Play (:116-146): the snap cue, then the chain grows on the Thud curve (340 ms); where it lands
    /// the leashed avatar thuds (1.18) under 22 gold and pink sparks, and the title lands (1.5) after 300 ms.
    /// Motion Off: the chain is simply there; Reduced plays at half size (WPF LeashFx.Amount). The heart tag swings
    /// only where ambient loops may run (WPF LeashFx.Swing).</summary>
    internal void Play()
    {
        LeashSound.Snap(DateTime.UtcNow);
        _amount = F.FeedMotionAmount;
        if (_amount <= 0 || RenderProof.Rendering) return;
        _swing = AmbientFxCanvas.Env.AllowAmbientLoops;
        Stop();
        _burst = false;
        Advance(0);
        _loop = new BeatLoop(this, seconds => Advance(seconds * 1000));
        _loop.Start();
    }
    internal void Stop() { _loop?.Stop(); _loop = null; }
    /// <summary>One frame of the snap at <paramref name="ms"/> since Play (tests step it).</summary>
    internal void Advance(double ms)
    {
        if (_amount <= 0) return;
        _chainScale.ScaleX = BillboardTiming.Thud(Math.Clamp(ms / 340, 0, 1));
        // Gentle visuals: the title's rise keeps the 300 ms floor (WPF shows it at once).
        var rise = GentleVisuals.RiseMs(0, GentleVisuals.On);
        _title.Opacity = ms < 300 ? 0 : rise <= 0 ? 1 : Math.Clamp((ms - 300) / rise, 0, 1);
        if (ms >= 300) _titleScale.ScaleX = _titleScale.ScaleY = 1 + 0.5 * _amount * (1 - BillboardTiming.Thud(Math.Clamp((ms - 300) / 340, 0, 1)));
        if (ms >= 340 && !_burst) Burst();
        if (_burst)
        {
            _impactScale.ScaleX = _impactScale.ScaleY = 1 + 0.18 * _amount * (1 - BillboardTiming.Thud(Math.Clamp((ms - 340) / 340, 0, 1)));
            for (int i = _particles.Count - 1; i >= 0; i--)
            {
                var (dot, x, y, length) = _particles[i];
                var p = Math.Clamp((ms - 340) / length, 0, 1);
                var q = 1 - (1 - p) * (1 - p);   // quadratic ease-out
                if (dot.RenderTransform is TranslateTransform t) { t.X = x * q; t.Y = (y + 10) * q; }
                dot.Opacity = 1 - p;
                if (p >= 1) { _sparks.Children.Remove(dot); _particles.RemoveAt(i); }
            }
        }
        // WPF LeashFx.Swing: -8 to 8 degrees, 1.5 s each way, sine.
        if (_swing) _tagTurn.Angle = -8 * Math.Cos(ms * Math.PI / 1500);
        if (ms >= 1200 && !_swing) Stop();
    }
    /// <summary>WPF LeashFx.Sparks/SparksAt (reach 60): dots fly out from the leashed avatar and fade.</summary>
    private void Burst()
    {
        _burst = true;
        var centre = _right.TranslatePoint(new Point(_right.Bounds.Width / 2, _right.Bounds.Height / 2), _sparks) ?? new Point(310, 42);
        var rng = new Random();
        for (int i = 0; i < (int)(22 * _amount); i++)
        {
            double size = 3 + rng.NextDouble() * 4, angle = rng.NextDouble() * Math.PI * 2, reach = (24 + rng.NextDouble() * 60) * _amount;
            var dot = new Ellipse { Width = size, Height = size, Fill = i % 3 == 2 ? F.Pink : F.Gold, IsHitTestVisible = false, Tag = "leash-spark", RenderTransform = new TranslateTransform() };
            Canvas.SetLeft(dot, centre.X - size / 2); Canvas.SetTop(dot, centre.Y - size / 2);
            _sparks.Children.Add(dot);
            _particles.Add((dot, Math.Cos(angle) * reach, Math.Sin(angle) * reach, 520 + rng.Next(320)));
        }
    }
    /// <summary>WPF LeashLook.Chain (190 x 14): alternating face and edge links.</summary>
    private static Canvas Chain()
    {
        var canvas = new Canvas { Width = 190, Height = 14, Tag = "leash-snap-chain" };
        var light = new SolidColorBrush(Color.FromRgb(0xD9, 0xC6, 0xFF));
        var dark = new SolidColorBrush(Color.FromRgb(0x9F, 0x86, 0xD0));
        for (int i = 0; i < 18; i++)
        {
            bool face = i % 2 == 0;
            var link = new Rectangle { Width = 15, Height = face ? 14 : 5.88, RadiusX = face ? 7 : 2, RadiusY = face ? 7 : 2,
                Stroke = face ? light : null, StrokeThickness = face ? 2.2 : 0, Fill = face ? Brushes.Transparent : dark };
            Canvas.SetLeft(link, i * 11 - 2); Canvas.SetTop(link, face ? 0 : 4.06);
            canvas.Children.Add(link);
        }
        return canvas;
    }
    /// <summary>WPF LeashLook.HeartTag (:254-276): the ring and the gold heart charm.</summary>
    private static Control HeartTag(double height)
    {
        var ring = new SolidColorBrush(Color.FromRgb(0xD9, 0xC6, 0xFF));
        var c = new Canvas { Width = 46, Height = 52, Children = {
            new Path { Data = Geometry.Parse("M23,0 V8"), Stroke = ring, StrokeThickness = 2 },
            new Path { Data = Geometry.Parse("M19.8,10 A3.2,3.2 0 1 0 26.2,10 A3.2,3.2 0 1 0 19.8,10 Z"), Stroke = ring, StrokeThickness = 2 },
            new Path { Data = Geometry.Parse("M23,50 C10,41 4,34 4,25.5 C4,19 8.6,14.5 14.4,14.5 C18,14.5 21.2,16.3 23,19.1 C24.8,16.3 28,14.5 31.6,14.5 C37.4,14.5 42,19 42,25.5 C42,34 36,41 23,50 Z"),
                Fill = F.Gold, Stroke = new SolidColorBrush(Color.FromRgb(0xFF, 0xF3, 0xC9)), StrokeThickness = 1.2 },
            new Path { Data = Geometry.Parse("M11,22 C12,19 14.5,17.5 17,17.5"), Stroke = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFB, 0xE8)),
                StrokeThickness = 2, StrokeLineCap = PenLineCap.Round } } };
        return new Viewbox { Height = height, Child = c, Tag = "leash-tag", HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(-4, 0, 0, -10) };
    }
}
