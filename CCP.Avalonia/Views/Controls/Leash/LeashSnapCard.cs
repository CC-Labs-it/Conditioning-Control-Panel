// WPF Controls/Leash/LeashSnapCard.cs:20-146 (leashed side; LeashSurfaces.ShowAsk :205-209 opens it on accept).
// Missing: LeashLook.Chain's drawn links (plain rings here) and the card drop shadow (:42).
using System;
using System.Threading;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Leash;
using F = ConditioningControlPanel.Avalonia.Views.Controls.FriendsDrawer;
namespace ConditioningControlPanel.Avalonia.Views.Controls.Leash;
/// <summary>THE SNAP: the two avatars, the chain shooting across, "Leashed." and both recaps.</summary>
public sealed class LeashSnapCard : Border
{
    private readonly Canvas _chain = new() { Width = 190, Height = 24, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Tag = "leash-snap-chain" };
    private readonly TextBlock _title;
    private readonly Control _right;
    private readonly Canvas _fx = new() { IsHitTestVisible = false, ClipToBounds = false };
    private static readonly Random Rng = new();
    internal LeashPerson Holder { get; }
    public event Action? Closed;
    internal LeashSnapCard(LeashPerson holder, LeashPerson leashed)
    {
        Holder = holder;
        Width = 380;
        Background = LeashAskCard.Card;
        BorderBrush = LeashAskCard.GoldDeep;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(18);
        Padding = new Thickness(18);
        Tag = "leash-snap";
        for (var i = 0; i < 10; i++)
        {
            var link = new Ellipse { Width = 24, Height = 13, Stroke = F.Gold, StrokeThickness = 2 };
            Canvas.SetLeft(link, i * 18.5); Canvas.SetTop(link, 5); _chain.Children.Add(link);
        }
        var row = new Grid { Height = 84, ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        // WPF :57-77: both avatars wear their look; the leashed one carries the swinging heart tag.
        row.Children.Add(F.Avatar(holder.Name, 68, null, holder.Look));
        Grid.SetColumn(_chain, 1); row.Children.Add(_chain);
        _right = F.Avatar(leashed.Name, 68, null, leashed.Look);
        var tag = HeartTag(26);
        var rightHost = new Grid { Children = { _right, tag } };
        Grid.SetColumn(rightHost, 2); row.Children.Add(rightHost);
        Swing(tag);
        _title = new TextBlock { Text = Loc.Get("leash_snap_title"), FontSize = 26, FontFamily = F.Display, FontWeight = FontWeight.SemiBold, Foreground = F.Gold,
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0), Tag = "leash-snap-title" };
        var recap = new UniformGrid { Columns = 2, Margin = new Thickness(0, 12, 0, 0),
            Children = { Recap(Loc.GetF("leash_snap_holder", holder.Name), F.Gold), Recap(Loc.GetF("leash_snap_leashed", leashed.Name), F.Mint) } };
        var ok = F.Pill(new TextBlock { Text = Loc.Get("leash_snap_ok"), FontFamily = F.Display, FontSize = 15 }, F.Pink, F.Text, "leash-snap-ok", F.Pink);
        ok.HorizontalAlignment = HorizontalAlignment.Stretch;
        ok.HorizontalContentAlignment = HorizontalAlignment.Center;
        ok.Padding = new Thickness(12, 9);
        ok.Margin = new Thickness(0, 16, 0, 0);
        ok.Click += (_, _) => Closed?.Invoke();
        Child = new Grid { Children = { new StackPanel { Children = { row, _title, recap, ok } }, _fx } };
    }
    private static Control Recap(string text, IBrush fg) => new Border
    {
        Background = F.Raised, CornerRadius = new CornerRadius(10), Padding = new Thickness(10), Margin = new Thickness(3),
        Child = new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = fg, TextWrapping = TextWrapping.Wrap },
    };
    /// <summary>WPF Play (:116-146): the snap cue, then the chain grows on the Thud curve; where it lands the
    /// leashed avatar thuds (1.18) under 22 gold and pink sparks, and the title lands (1.5) after 300 ms.
    /// Motion Off: the chain is simply there; Reduced plays at half size (WPF LeashFx.Amount).</summary>
    internal void Play()
    {
        LeashSound.Snap(DateTime.UtcNow);
        var k = F.FeedMotionAmount;
        if (k <= 0) return;
        var scale = new ScaleTransform(0, 1) { Transitions = new Transitions { Thud(ScaleTransform.ScaleXProperty) } };
        _chain.RenderTransformOrigin = new RelativePoint(0, 0.5, RelativeUnit.Relative);
        _chain.RenderTransform = scale;
        _title.Opacity = 0;
        Dispatcher.UIThread.Post(() => scale.ScaleX = 1);
        DispatcherTimer.RunOnce(() => { ThudIn(_right, 1.18, k); Sparks((int)(22 * k), k); }, TimeSpan.FromMilliseconds(340));
        DispatcherTimer.RunOnce(() => { _title.Opacity = 1; ThudIn(_title, 1.5, k); }, TimeSpan.FromMilliseconds(300));
    }
    // WPF BezierEase.Thud: 340 ms, cubic-bezier(.2,1.5,.4,1).
    private static DoubleTransition Thud(AvaloniaProperty p) =>
        new() { Property = p, Duration = TimeSpan.FromMilliseconds(340), Easing = new SplineEasing(0.2, 1.5, 0.4, 1) };
    /// <summary>WPF LeashFx.Thud: lands from a little larger with the house overshoot.</summary>
    private static void ThudIn(Control el, double from, double k)
    {
        var start = 1 + (from - 1) * k;
        var s = new ScaleTransform(start, start) { Transitions = new Transitions { Thud(ScaleTransform.ScaleXProperty), Thud(ScaleTransform.ScaleYProperty) } };
        el.RenderTransformOrigin = RelativePoint.Center;
        el.RenderTransform = s;
        Dispatcher.UIThread.Post(() => { s.ScaleX = 1; s.ScaleY = 1; });
    }
    /// <summary>WPF LeashFx.Sparks/SparksAt (reach 60): dots fly out from the leashed avatar and fade.</summary>
    private void Sparks(int count, double k)
    {
        if (_right.TranslatePoint(new Point(_right.Bounds.Width / 2, _right.Bounds.Height / 2), _fx) is not { } c) return;
        IBrush[] colors = { F.Gold, F.Gold, F.Pink };
        for (var i = 0; i < count; i++)
        {
            var size = 3 + Rng.NextDouble() * 4;
            var color = colors[i % colors.Length];
            var dur = TimeSpan.FromMilliseconds(520 + Rng.Next(320));
            var ease = new QuadraticEaseOut();
            var t = new TranslateTransform { Transitions = new Transitions {
                new DoubleTransition { Property = TranslateTransform.XProperty, Duration = dur, Easing = ease },
                new DoubleTransition { Property = TranslateTransform.YProperty, Duration = dur, Easing = ease } } };
            var dot = new Ellipse { Width = size, Height = size, Fill = color, RenderTransform = t, IsHitTestVisible = false, Tag = "leash-spark",
                Effect = new DropShadowEffect { Color = ((ISolidColorBrush)color).Color, BlurRadius = 6, OffsetX = 0, OffsetY = 0, Opacity = 0.9 },
                Transitions = new Transitions { new DoubleTransition { Property = OpacityProperty, Duration = dur } } };
            Canvas.SetLeft(dot, c.X - size / 2); Canvas.SetTop(dot, c.Y - size / 2);
            _fx.Children.Add(dot);
            var ang = Rng.NextDouble() * Math.PI * 2;
            var dist = (60 * 0.4 + Rng.NextDouble() * 60) * k;
            Dispatcher.UIThread.Post(() => { t.X = Math.Cos(ang) * dist; t.Y = Math.Sin(ang) * dist + 10; dot.Opacity = 0; });
            DispatcherTimer.RunOnce(() => _fx.Children.Remove(dot), dur);
        }
    }
    /// <summary>WPF LeashFx.Swing (:118-132): -8 to 8 degrees, 1.5 s each way, forever, ambient loops only.</summary>
    private static void Swing(Control tag)
    {
        if (!AmbientFxCanvas.Env.AllowAmbientLoops) return;
        tag.RenderTransformOrigin = new RelativePoint(0.5, 0, RelativeUnit.Relative);
        tag.RenderTransform = new RotateTransform();
        var swing = new Animation { Duration = TimeSpan.FromSeconds(1.5), IterationCount = IterationCount.Infinite,
            PlaybackDirection = PlaybackDirection.Alternate, Easing = new SineEaseInOut(),
            Children = {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(RotateTransform.AngleProperty, -8d) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(RotateTransform.AngleProperty, 8d) } } } };
        CancellationTokenSource? run = null;
        tag.AttachedToVisualTree += (_, _) => { run?.Cancel(); run = new CancellationTokenSource(); _ = swing.RunAsync(tag, run.Token); };
        tag.DetachedFromVisualTree += (_, _) => { run?.Cancel(); run = null; };
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
