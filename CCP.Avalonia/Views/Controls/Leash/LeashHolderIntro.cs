// WPF Controls/Leash/Explain/LeashExplainer.cs:63 and LeashExplainCard.cs:99.
using System;
using ConditioningControlPanel.Avalonia.Helpers;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using ConditioningControlPanel.Localization;
using F = ConditioningControlPanel.Avalonia.Views.Controls.FriendsDrawer;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Leash;

internal sealed class LeashHolderIntro : Window
{
    private readonly Border[] _panels = new Border[4];
    private readonly TranslateTransform[] _rise = new TranslateTransform[4];
    private readonly ScaleTransform[] _bars = new ScaleTransform[3];
    private readonly ScaleTransform _eye = new(), _thud = new();
    private readonly RotateTransform _bladeA = new(), _bladeB = new();
    private readonly TranslateTransform _left = new(), _right = new();
    private readonly Ellipse[] _bits = new Ellipse[6];
    private BeatLoop? _loop;
    private bool _played;
    internal bool MotionActive => _loop?.IsRunning == true;
    internal Action<PointerPressedEventArgs>? DragOverride { get; set; }
    /// <summary>--render-all (RenderProof): the holder card offering to a sample friend; the offer does nothing.</summary>
    internal LeashHolderIntro() : this("Mia", () => { }) { }
    internal LeashHolderIntro(string name, Action? offer)
    {
        Title = offer == null || string.IsNullOrWhiteSpace(name) ? Loc.Get("leash_explain_title") : Loc.GetF("leash_explain_offer_title", name.Trim());
        Tag = "leash-explainer";
        WindowDecorations = WindowDecorations.None;
        Width = 640; SizeToContent = SizeToContent.Height; CanResize = false;
        ShowInTaskbar = false; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = F.Foot;
        var root = new StackPanel { Margin = new Thickness(18), Spacing = 12, Background = Brushes.Transparent };
        var title = F.Label(Title, 20, F.Text, F.Display, FontWeight.SemiBold);
        title.TextWrapping = TextWrapping.Wrap;
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        header.Children.Add(title);
        var x = F.Pill(new IconGlyph { Kind = IconKind.Dismiss, Size = 12 }, F.Raised, F.Text, "leash-explain-close"); x.Click += (_, _) => Close();
        global::Avalonia.Automation.AutomationProperties.SetName(x, Loc.Get("btn_close"));   // an icon-only button needs its name
        Grid.SetColumn(x, 1); header.Children.Add(x); root.Children.Add(header);
        var pictures = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*") };
        // Flat, finished-state drawings: offer, collar and level, eye and day, scissors cutting.
        string[] paths = {
            "M 17,20 A 8,8 0 1 1 33,20 A 8,8 0 1 1 17,20 M 10,58 Q 10,35 25,35 Q 40,35 40,58 M 40,48 C 55,55 58,25 73,38 M 73,38 A 6,6 0 1 1 85,38 A 6,6 0 1 1 73,38",
            "M 20,18 A 8,8 0 1 1 36,18 A 8,8 0 1 1 20,18 M 15,55 Q 15,32 28,32 Q 41,32 41,55 M 19,35 L 37,35",
            "M 20,59 L 28,59 M 33,59 L 41,59 M 46,59 L 54,59 M 59,59 L 67,59 M 72,59 L 80,59",
            "M 10,58 L 42,58" };
        for (int i = 0; i < 4; i++)
        {
            var drawing = new Path { Data = Geometry.Parse(paths[i]), Stroke = F.Gold, StrokeThickness = 2.5, Stretch = Stretch.Uniform, Width = 100, Height = 70, Tag = "leash-explain-art:" + (i + 1) };
            var caption = F.Label(Loc.Get($"leash_explain_step{i + 1}_holder"), 13, i == 3 ? F.Mint : F.Text, F.Display, FontWeight.SemiBold);
            caption.TextWrapping = TextWrapping.Wrap; caption.TextAlignment = TextAlignment.Center; caption.HorizontalAlignment = HorizontalAlignment.Center;
            var panel = new Border { Tag = "leash-explain-panel:" + (i + 1), Background = F.Raised, BorderBrush = i == 3 ? F.Mint : F.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Margin = new Thickness(3), Padding = new Thickness(6), Child = new StackPanel { Spacing = 6, Children = { drawing, caption } } };
            var stage = new Canvas { Width = 100, Height = 70 };
            ((StackPanel)panel.Child!).Children[0] = stage;
            drawing.Stretch = Stretch.None; stage.Children.Add(drawing);
            if (i == 1) for (int b = 0; b < 3; b++)
            {
                _bars[b] = new ScaleTransform();
                stage.Children.Add(Part($"M {53 + b * 12},53 L {53 + b * 12},{42 - b * 10}", _bars[b], "leash-bar:" + b, new RelativePoint(0.5, 53.0 / 70, RelativeUnit.Relative)));
            }
            if (i == 2) stage.Children.Add(Part("M 15,25 Q 50,-5 85,25 Q 50,55 15,25 Z M 50,18 A 7,7 0 1 1 49.9,18", _eye, "leash-eye", new RelativePoint(0.5, 25.0 / 70, RelativeUnit.Relative), F.Lilac));
            if (i == 3)
            {
                drawing.RenderTransform = _left;
                stage.Children.Add(Part("M 58,58 L 90,58", _right, "leash-cut-right", RelativePoint.Center));
                stage.Children.Add(Part("M 37,26 A 7,7 0 1 1 36.9,26 M 43,29 L 76,49", _bladeA, "leash-blade-a", new RelativePoint(0.54, 0.51, RelativeUnit.Relative), F.Mint));
                stage.Children.Add(Part("M 37,45 A 7,7 0 1 1 36.9,45 M 43,42 L 76,22", _bladeB, "leash-blade-b", new RelativePoint(0.54, 0.51, RelativeUnit.Relative), F.Mint));
                for (int b = 0; b < _bits.Length; b++) { _bits[b] = new Ellipse { Width = 4, Height = 4, Fill = F.Gold, IsVisible = false }; stage.Children.Add(_bits[b]); }
            }
            _panels[i] = panel; _rise[i] = new TranslateTransform();
            var group = new TransformGroup(); group.Children.Add(_rise[i]); if (i == 3) group.Children.Add(_thud); panel.RenderTransform = group;
            panel.RenderTransformOrigin = RelativePoint.Center;
            Grid.SetColumn(panel, i); pictures.Children.Add(panel);
        }
        root.Children.Add(pictures);
        foreach (var key in new[] { "leash_explain_holder_see", "leash_explain_holder_cut" })
        {
            var line = F.Label(Loc.Get(key), 13, F.Text); line.TextWrapping = TextWrapping.Wrap; line.TextTrimming = TextTrimming.None; root.Children.Add(line);
        }
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        var close = F.Pill(Loc.Get(offer == null ? "leash_explain_got_it" : "leash_explain_not_now"), F.Raised, F.Text, "leash-explain-not-now");
        close.Click += (_, _) => Close(); buttons.Children.Add(close);
        if (offer != null)
        {
            var send = F.Pill(Loc.Get("leash_explain_offer"), F.Raised, F.Gold, "leash-explain-offer");
            send.Click += (_, _) => { Close(); offer(); }; buttons.Children.Add(send);
        }
        root.Children.Add(buttons);
        Content = new ScrollViewer { Content = root, MaxHeight = 700 };
        AddHandler(KeyDownEvent, (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } }, RoutingStrategies.Tunnel);
        root.PointerPressed += (_, e) => { if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) try { if (DragOverride is { } drag) drag(e); else BeginMoveDrag(e); } catch { } };
        Opened += (_, _) => StartMotion();
        Closed += (_, _) => StopMotion();
    }
    private static Path Part(string geometry, Transform transform, string tag, RelativePoint origin, IBrush? stroke = null) => new()
    {
        Data = Geometry.Parse(geometry), Stroke = stroke ?? F.Gold, StrokeThickness = 2.5, Width = 100, Height = 70,
        Stretch = Stretch.None, RenderTransform = transform, RenderTransformOrigin = origin, Tag = tag,
    };
    private void StopMotion() { _loop?.Stop(); _loop = null; }
    private void StartMotion()
    {
        if (_played) return;
        _played = true;
        // Gentle visuals: the 220 ms panel rise and the snip burst sit under the 300 ms floor, so the
        // finished drawings show at once instead.
        // --render-all draws the finished cards, not a frame of the rise.
        if (!AmbientFxCanvas.Env.AllowTransitions || CoreSettings.Current.GentleVisuals || RenderProof.Rendering) return;
        // On the window's shared beat (BeatLoop), not an own frame timer; it parks while minimised.
        _loop = new BeatLoop(this, seconds =>
        {
            var ms = seconds * 1000;
            if (ms > 0 && (!AmbientFxCanvas.Env.AllowTransitions || ms >= 1550)) { MotionFrame(2000); StopMotion(); return; }
            MotionFrame(ms);
        });
        _loop.Start();
    }
    private static double Progress(double t, double duration) => Math.Clamp(t / duration, 0, 1);
    private static double Back(double t) { t -= 1; return 1 + 1.55 * t * t * t + 0.55 * t * t; }
    // WPF LeashExplainCard.Play: stagger, bars 260 ms, blink 520 ms, snip 760 ms.
    internal void MotionFrame(double ms)
    {
        for (int i = 0; i < 4; i++) { var t = Math.Max(0, ms - 40 * i); _panels[i].Opacity = 1 - Math.Pow(1 - Progress(t, 220), 2); _rise[i].Y = 10 * Math.Pow(1 - Progress(t, 260), 2); }
        for (int i = 0; i < 3; i++) _bars[i].ScaleY = ms < 260 ? 1 : 0.2 + 0.8 * Back(Progress(ms - 260 - 70 * i, 340));
        double blink = ms - 520; _eye.ScaleY = blink < 0 || blink >= 260 ? 1 : blink < 110 ? 1 - 0.9 * Progress(blink, 110) : 0.1 + 0.9 * Progress(blink - 110, 150);
        double cut = ms - 760, open = cut < 0 || cut >= 300 ? 0 : cut < 160 ? Math.Sin(Progress(cut, 160) * Math.PI / 2) : 1 - Math.Pow(Progress(cut - 160, 140), 3);
        _bladeA.Angle = -18 * open; _bladeB.Angle = 18 * open;
        double joined = cut < 0 || cut >= 640 ? 0 : cut < 300 ? 5 : 5 * (1 - Progress(cut - 300, 340));
        _left.X = joined; _right.X = -joined;
        _thud.ScaleX = _thud.ScaleY = cut < 300 || cut >= 640 ? 1 : 1 + 0.05 * (1 - Progress(cut - 300, 340));
        for (int i = 0; i < _bits.Length; i++)
        {
            var bit = _bits[i]; bit.IsVisible = AmbientFxCanvas.Env.AllowParticles && cut >= 300 && cut < 740;
            if (bit.IsVisible) { var t = Progress(cut - 300, 440); Canvas.SetLeft(bit, 50 + (i - 2.5) * 10 * t); Canvas.SetTop(bit, 58 - (22 - Math.Abs(i - 2.5) * 3) * t); bit.Opacity = 1 - t; }
        }
    }

}
