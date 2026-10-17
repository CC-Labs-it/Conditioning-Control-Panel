using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using ConditioningControlPanel.Controls.Friends;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Leash;

namespace ConditioningControlPanel.Controls.Leash;

/// <summary>
/// A "?" slip (owner-approved "ink slips", 2026-10-17): a paper card that says what the other
/// player gets when the holder presses a button. Title, a red level stamp, "YOU [button] -> THEM
/// [pictogram]", a caption and a small limits line. Code-built; the pictograms are the mockup's
/// SVGs redrawn as WPF shapes on a 130x84 canvas.
/// </summary>
public sealed class LeashHelpSlip : Border
{
    internal static readonly Brush Paper = FriendsLook.Frozen(Color.FromRgb(0xF3, 0xEE, 0xE4));
    internal static readonly Brush Ink = FriendsLook.Frozen(Color.FromRgb(0x1E, 0x1B, 0x22));
    internal static readonly Brush Stamp = FriendsLook.Frozen(Color.FromRgb(0xC2, 0x30, 0x4A));
    internal static readonly Brush Muted = FriendsLook.Frozen(Color.FromRgb(0x5A, 0x54, 0x62));
    internal static readonly Brush Green = FriendsLook.Frozen(Color.FromRgb(0x2F, 0x7A, 0x4F));
    internal static readonly Brush PinkFill = FriendsLook.Frozen(Color.FromRgb(0xF2, 0xB6, 0xCF));
    private static readonly FontFamily Sans = new("Segoe UI");

    public LeashHelpEntry Entry { get; }

    public LeashHelpSlip(LeashHelpEntry entry, string theirName)
    {
        Entry = entry;
        Width = 300;
        Background = Paper;
        CornerRadius = new CornerRadius(6);
        Padding = new Thickness(16);
        BorderBrush = FriendsLook.Frozen(Color.FromRgb(0xD8, 0xCF, 0xBF));
        BorderThickness = new Thickness(0, 0, 0, 1);
        Tag = "leash-slip:" + entry.Id;
        Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 6, Direction = 270, Opacity = 0.25, Color = Color.FromRgb(0x1E, 0x1B, 0x22) };

        var sp = new StackPanel();

        // Title and stamp.
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(new TextBlock
        {
            Text = Loc.Get(entry.TitleKey),
            FontFamily = Sans,
            FontWeight = FontWeights.ExtraBold,
            FontSize = 18,
            Foreground = Ink,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        var stamp = new Border
        {
            BorderBrush = Stamp,
            BorderThickness = new Thickness(2),
            Padding = new Thickness(6, 1, 6, 1),
            Margin = new Thickness(8, 0, 2, 0),
            VerticalAlignment = VerticalAlignment.Center,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new RotateTransform(-4),
            Tag = "leash-slip-stamp",
            Child = new TextBlock
            {
                Text = LeashHelpCatalog.StampText(entry.Stamp, Loc.Get),
                FontFamily = FriendsLook.Mono,
                FontWeight = FontWeights.Bold,
                FontSize = 10,
                Foreground = Stamp,
            },
        };
        Grid.SetColumn(stamp, 1);
        head.Children.Add(stamp);
        sp.Children.Add(head);

        // YOU [button] -> THEM [pictogram].
        var pic = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        var you = Side(Loc.Get("leash_slip_you"), new Border
        {
            MinWidth = 64,
            Height = 32,
            Padding = new Thickness(8, 0, 8, 0),
            BorderBrush = Ink,
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(8),
            Child = new TextBlock
            {
                Text = LeashHelpCatalog.YouLabel(entry, Loc.Get),
                FontFamily = Sans,
                FontWeight = FontWeights.SemiBold,
                FontSize = 12,
                Foreground = Ink,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        });
        you.VerticalAlignment = VerticalAlignment.Center;
        pic.Children.Add(you);
        var arrow = new Canvas { Width = 28, Height = 16, Margin = new Thickness(6, 14, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        arrow.Children.Add(P("M2 8 H22 M16 3 L23 8 L16 13", null, Ink, 2));
        pic.Children.Add(arrow);
        pic.Children.Add(Side(Loc.Get("leash_slip_them"), Pictogram(entry.Pictogram)));
        sp.Children.Add(pic);

        // Caption and limits.
        var caption = Loc.Get(entry.CaptionKey);
        if (entry.CaptionNamesThem) caption = string.Format(CultureInfo.InvariantCulture, caption, theirName);
        sp.Children.Add(new TextBlock
        {
            Text = caption,
            FontFamily = Sans,
            FontSize = 13.5,
            LineHeight = 19,
            Foreground = Ink,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 12, 0, 0),
            Tag = "leash-slip-caption",
        });
        sp.Children.Add(new TextBlock
        {
            Text = Loc.Get(entry.LimitsKey),
            FontFamily = FriendsLook.Mono,
            FontSize = 11.5,
            Foreground = Muted,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 0),
            Tag = "leash-slip-limits",
        });
        Child = sp;
    }

    internal string CaptionText => ((TextBlock)((StackPanel)Child).Children[2]).Text;

    private static StackPanel Side(string label, FrameworkElement body)
    {
        var s = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        s.Children.Add(new TextBlock
        {
            Text = label,
            FontFamily = FriendsLook.Mono,
            FontWeight = FontWeights.Bold,
            FontSize = 10,
            Foreground = Ink,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 5),
        });
        body.HorizontalAlignment = HorizontalAlignment.Center;
        s.Children.Add(body);
        return s;
    }

    // ---- pictograms (the mockup's SVGs, 130x84) ----------------------------------------

    /// <summary>The pictogram for <paramref name="id"/>; an unknown id draws an empty frame.</summary>
    internal static Canvas Pictogram(string id)
    {
        var c = new Canvas { Width = 130, Height = 84, Tag = "leash-slip-pic:" + id, ClipToBounds = false };
        void Add(UIElement e) => c.Children.Add(e);
        switch (id)
        {
            case "assign":
                Add(R(8, 6, 114, 72, 5, null, Ink, 2));
                Add(R(18, 18, 10, 10, 2, null, Stamp, 2));
                Add(P("M34 23 H84", null, Ink, 2));
                Add(R(18, 36, 10, 10, 2, null, Ink, 2));
                Add(P("M34 41 H70", null, Ink, 2));
                Add(R(66, 56, 50, 14, 3, Ink, null, 0));
                break;
            case "tug":
                var g = new Canvas { Width = 130, Height = 84, RenderTransform = new RotateTransform(-4, 65, 42) };
                g.Children.Add(R(12, 8, 106, 66, 5, null, Ink, 2));
                g.Children.Add(R(64, 54, 48, 14, 3, Ink, null, 0));
                Add(g);
                Add(P("M4 30 Q8 36 4 42 M126 34 Q122 40 126 46", null, Stamp, 2));
                Add(C(32, 34, 5, null, Stamp, 2));
                Add(C(42, 30, 5, null, Stamp, 2));
                Add(C(52, 26, 5, null, Stamp, 2));
                break;
            case "letgo":
                Add(C(30, 30, 9, null, Ink, 2.4));
                Add(C(48, 30, 9, null, Ink, 2.4));
                Add(P("M62 22 L70 30 M66 18 L70 24", null, Stamp, 2.2));
                Add(C(86, 30, 9, null, Ink, 2.4));
                Add(R(20, 54, 34, 16, 8, null, Ink, 2));
                Add(C(46, 62, 5, Ink, null, 0));
                Add(T(60, 66, 11, Ink, "panic on"));
                break;
            case "lines":
                Add(R(36, 20, 70, 50, 4, Paper, Ink, 2));
                Add(R(28, 14, 70, 50, 4, Paper, Ink, 2));
                Add(R(20, 8, 70, 50, 4, Paper, Ink, 2));
                Add(P("M30 22 H78 M30 32 H70 M30 42 H74", null, Ink, 2));
                Add(T(96, 80, 12, Stamp, "x5"));
                break;
            case "pink":
                Add(R(8, 6, 114, 72, 5, PinkFill, Ink, 2));
                Add(R(22, 18, 30, 22, 2, null, Ink, 2));
                Add(R(70, 40, 34, 24, 2, null, Ink, 2));
                Add(T(84, 22, 11, Ink, "15:00"));
                break;
            case "bubbles":
                Add(R(8, 6, 114, 72, 5, null, Ink, 2));
                Add(C(34, 30, 10, null, Stamp, 2));
                Add(C(62, 48, 13, null, Stamp, 2));
                Add(C(92, 26, 8, null, Stamp, 2));
                Add(C(96, 58, 6, null, Stamp, 2));
                Add(T(16, 72, 11, Ink, "12/100"));
                break;
            case "detention":
                Add(R(8, 6, 114, 72, 5, null, Ink, 2));
                Add(R(18, 16, 26, 20, 2, Ink, null, 0));
                Add(R(52, 40, 22, 16, 2, Ink, null, 0));
                var dash = P("M18 64 H60", null, Stamp, 2);
                dash.StrokeDashArray = new DoubleCollection { 1.5, 2 };
                Add(dash);
                Add(C(98, 40, 15, null, Ink, 2));
                Add(P("M98 32 V40 L104 44", null, Ink, 2));
                break;
            case "video":
                Add(R(8, 6, 114, 72, 5, null, Ink, 2));
                Add(R(13, 10, 104, 64, 3, Ink, null, 0));
                Add(P("M58 30 L76 42 L58 54 Z", Paper, null, 0));
                Add(P("M104 16 L112 24 M112 16 L104 24", null, Paper, 2));
                Add(P("M100 28 L116 12", null, Stamp, 2.4));
                break;
            case "chaster":
            case "credit":
                bool credit = id == "credit";
                Add(R(34, 34, 40, 34, 5, null, Ink, 2.4));
                Add(P(credit ? "M42 34 V24 a12 12 0 0 1 24 0 V30" : "M42 34 V24 a12 12 0 0 1 24 0 V34", null, Ink, 2.4));
                Add(C(54, 50, 4, Ink, null, 0));
                Add(T(80, 40, 14, credit ? Green : Stamp, credit ? "-30" : "+30"));
                break;
            case "sticker":
                Add(P("M14 66 H116", null, Ink, 2.4));
                Add(P("M42 14 L48 30 L65 30 L51 40 L56 57 L42 47 L28 57 L33 40 L19 30 L36 30 Z", Stamp, null, 0));
                Add(C(82, 50, 12, null, Ink, 2));
                Add(R(100, 42, 14, 20, 3, null, Ink, 2));
                break;
            case "praise":
                Add(R(8, 6, 114, 72, 5, null, Ink, 2));
                Add(R(40, 48, 74, 22, 4, Ink, null, 0));
                Add(P("M48 59 H104", null, Paper, 2));
                Add(P("M20 30 Q26 22 20 14 M28 34 Q38 22 28 10", null, Stamp, 2));
                break;
            case "pardon":
                Add(P("M16 22 H112 V36 a6 6 0 0 0 0 12 V62 H16 V48 a6 6 0 0 0 0 -12 Z", null, Ink, 2.2));
                Add(T(36, 47, 14, Stamp, "PARDON"));
                break;
            default:
                Add(R(8, 6, 114, 72, 5, null, Ink, 2));
                break;
        }
        return c;
    }

    private static Rectangle R(double x, double y, double w, double h, double rx, Brush? fill, Brush? stroke, double sw)
    {
        var r = new Rectangle { Width = w, Height = h, RadiusX = rx, RadiusY = rx, Fill = fill, Stroke = stroke, StrokeThickness = sw };
        Canvas.SetLeft(r, x);
        Canvas.SetTop(r, y);
        return r;
    }

    private static Ellipse C(double cx, double cy, double r, Brush? fill, Brush? stroke, double sw)
    {
        var e = new Ellipse { Width = r * 2, Height = r * 2, Fill = fill, Stroke = stroke, StrokeThickness = sw };
        Canvas.SetLeft(e, cx - r);
        Canvas.SetTop(e, cy - r);
        return e;
    }

    private static Path P(string d, Brush? fill, Brush? stroke, double sw) => new()
    {
        Data = Geometry.Parse(d),
        Fill = fill,
        Stroke = stroke,
        StrokeThickness = sw,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
        StrokeDashCap = PenLineCap.Round,
        StrokeLineJoin = PenLineJoin.Round,
    };

    /// <summary>SVG text at baseline (x, y): the TextBlock's top sits about one cap height up.</summary>
    private static TextBlock T(double x, double y, double size, Brush fg, string text)
    {
        var t = new TextBlock { Text = text, FontFamily = FriendsLook.Mono, FontWeight = FontWeights.Bold, FontSize = size, Foreground = fg };
        Canvas.SetLeft(t, x);
        Canvas.SetTop(t, y - size * 0.95);
        return t;
    }
}

/// <summary>
/// The small "?" that opens a <see cref="LeashHelpSlip"/>. Hover opens, a click pins (and a second
/// click unpins), the slip closes when the pointer leaves an unpinned slip or on any press
/// elsewhere. Never bound to Escape (the panic key). The popup is StaysOpen with no capture, and
/// drops its topmost flag on open (AGENTS.md popup rules). The "?" handles its own click, so the
/// command next to it never fires.
/// </summary>
public static class LeashHelpButton
{
    public static Button Create(LeashHelpEntry entry, Func<string> theirName, double size = 16)
    {
        var t = new TextBlock
        {
            Text = "?",
            FontFamily = FriendsLook.Display,
            FontWeight = FontWeights.Bold,
            FontSize = size * 0.62,
            Foreground = FriendsLook.LilacBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        // Opaque, so a "?" that sits on a button's corner reads as a badge, not a hole.
        var b = FriendsLook.Pill(t, FriendsLook.Frozen(FriendsLook.Rgb(0x24, 0x18, 0x38)), FriendsLook.LilacBrush,
            FriendsLook.Frozen(Color.FromArgb(0x66, 0xB9, 0x9C, 0xFF)), size / 2, new Thickness(0));
        b.Width = size;
        b.Height = size;
        b.Focusable = false;
        b.VerticalAlignment = VerticalAlignment.Center;
        b.Tag = "leash-slip-help:" + entry.Id;
        var ctl = new Controller(b, entry, theirName);
        b.Click += (_, e) => { e.Handled = true; ctl.TogglePin(); };
        return b;
    }

    /// <summary>The slip as hover content for something that cannot host a button (the Let go
    /// menu row): a ToolTip with the paper card, no chrome.</summary>
    public static ToolTip AsToolTip(LeashHelpEntry entry, string theirName) => new()
    {
        Content = new LeashHelpSlip(entry, theirName),
        Background = Brushes.Transparent,
        BorderThickness = new Thickness(0),
        Padding = new Thickness(0, 0, 8, 10),
        HasDropShadow = false,
        Placement = PlacementMode.Right,
    };

    private sealed class Controller
    {
        private readonly Button _b;
        private readonly LeashHelpEntry _entry;
        private readonly Func<string> _name;
        private Popup? _popup;
        private bool _pinned;
        private UIElement? _root;
        private readonly DispatcherTimer _leave;

        public Controller(Button b, LeashHelpEntry entry, Func<string> name)
        {
            _b = b;
            _entry = entry;
            _name = name;
            _leave = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
            _leave.Tick += (_, _) => { _leave.Stop(); if (!_pinned && !Over()) Close(); };
            b.MouseEnter += (_, _) => { _leave.Stop(); Open(); };
            b.MouseLeave += (_, _) => _leave.Start();
            b.IsVisibleChanged += (_, e) => { if (e.NewValue is false) Close(); };
            b.Unloaded += (_, _) => Close();
        }

        private bool Over() => _b.IsMouseOver || _popup?.Child is UIElement c && c.IsMouseOver;

        public void TogglePin()
        {
            if (_pinned) { _pinned = false; Close(); return; }
            _pinned = true;
            Open();
        }

        private void Open()
        {
            if (_popup is { IsOpen: true }) return;
            try
            {
                var slip = new LeashHelpSlip(_entry, _name());
                var holder = new Border { Padding = new Thickness(0, 4, 8, 12), Background = Brushes.Transparent, Child = slip };
                holder.MouseEnter += (_, _) => _leave.Stop();
                holder.MouseLeave += (_, _) => _leave.Start();
                holder.PreviewMouseDown += (_, e) => { _pinned = true; };
                _popup = new Popup
                {
                    Child = holder,
                    AllowsTransparency = true,
                    StaysOpen = true,
                    Placement = PlacementMode.Bottom,
                    PlacementTarget = _b,
                    PopupAnimation = PopupAnimation.None,
                    Focusable = false,
                };
                _popup.Opened += (_, _) => { DropTopmost(); HookRoot(); };
                _popup.Closed += (_, _) => UnhookRoot();
                _popup.IsOpen = true;
            }
            catch (Exception ex) { App.Logger?.Debug("[Leash] help slip open failed: {E}", ex.Message); }
        }

        private void Close()
        {
            _leave.Stop();
            _pinned = false;
            if (_popup != null) _popup.IsOpen = false;
            _popup = null;
        }

        /// <summary>A press anywhere else in the window that hosts the "?" folds the slip.</summary>
        private void HookRoot()
        {
            UnhookRoot();
            _root = PresentationSource.FromVisual(_b)?.RootVisual as UIElement;
            if (_root != null) _root.PreviewMouseDown += OnRootDown;
        }

        private void UnhookRoot()
        {
            if (_root != null) _root.PreviewMouseDown -= OnRootDown;
            _root = null;
        }

        private void OnRootDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is DependencyObject d && IsOn(d)) return;
            Close();
        }

        private bool IsOn(DependencyObject d)
        {
            for (var cur = d; cur != null; cur = cur is Visual || cur is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(cur) : LogicalTreeHelper.GetParent(cur))
                if (ReferenceEquals(cur, _b)) return true;
            return false;
        }

        private void DropTopmost()
        {
            try
            {
                if (_popup?.Child is { } c && PresentationSource.FromVisual(c) is HwndSource src && src.Handle != IntPtr.Zero)
                    SetWindowPos(src.Handle, new IntPtr(-2), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
            }
            catch (Exception ex) { App.Logger?.Debug("[Leash] help slip topmost drop failed: {E}", ex.Message); }
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    }
}
