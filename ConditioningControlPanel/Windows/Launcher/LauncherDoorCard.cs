using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Safety;
using Serilog;

namespace ConditioningControlPanel.Launcher;

/// <summary>What a door card shows. The window fills it; the card only paints and moves.</summary>
internal sealed record DoorCardSpec(
    string Title,
    int Count,
    string CountFormat,
    Color Hue,
    IReadOnlyList<ImageSource> Arts,
    Color Surface,
    Brush Rim,
    Brush CountBrush,
    Color TitleRest,
    FrameworkElement? Stamp = null,
    FrameworkElement? Extra = null,
    bool Glow = true);

/// <summary>
/// One door on the launcher's right column (GAMES, TOOLS): the card, its art reel and every
/// beat it plays. Built in code so the render suite can draw it without the window.
///
/// <para>Rails (JUICE-PLAYBOOK, AGENTS.md FX perf): only transforms and opacity move on the
/// card; the glow is a cached sibling behind it whose OPACITY breathes, never its Effect; the
/// reel's clocks run at 30 fps and only between <see cref="StartReel"/> and
/// <see cref="StopReel"/>; every fade goes through <see cref="GentleVisuals.FadeMs"/> so it can
/// only lengthen; reduced motion keeps a still picture and plain fades.</para>
/// </summary>
internal sealed class LauncherDoorCard
{
    internal const double Radius = 20;
    private const double GlowRest = 0.0;

    /// <summary>The launcher's rounded display face. Built against the app's pack root: a code
    /// FontFamily has no BaseUri, and a bare "/Fonts/#Fredoka" made in code falls back to Segoe UI.</summary>
    internal static readonly FontFamily DisplayFont =
        new(new Uri("pack://application:,,,/ConditioningControlPanel;component/"), "./Fonts/#Fredoka, Segoe UI");

    private readonly DoorCardSpec _spec;
    private readonly ScaleTransform _rootScale = new(1, 1);
    private readonly TranslateTransform _rootSlide = new();
    private readonly ScaleTransform _zoom = new(1, 1);
    private readonly TranslateTransform _chevronSlide = new();
    private readonly ScaleTransform _barScale = new(1, 1);
    private readonly SolidColorBrush _titleBrush;
    private readonly Border? _glow;
    private readonly Border _tint;
    private readonly Border _mask;
    private readonly Image[] _layers = new Image[2];
    private readonly TextBlock _count;
    private readonly ScaleTransform? _stampScale;

    internal Grid Root { get; }
    internal Border Card => _card;
    private readonly DoorCardBorder _card;

    /// <summary>What UI Automation's Invoke runs on this door (the window passes its open).</summary>
    internal Action? Invoked { get => _card.Invoked; set => _card.Invoked = value; }
    internal Color Hue => _spec.Hue;

    internal LauncherDoorCard(DoorCardSpec spec)
    {
        _spec = spec;
        var hue = spec.Hue;
        _titleBrush = new SolidColorBrush(spec.TitleRest);

        Root = new Grid
        {
            Margin = new Thickness(9),
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new TransformGroup { Children = { _rootScale, _rootSlide } },
        };

        // The glow: a soft hue slab behind the card, blurred ONCE and cached. Hover moves its
        // opacity only, so the blur is never recomputed.
        if (spec.Glow)
        {
            _glow = new Border
            {
                CornerRadius = new CornerRadius(Radius),
                Background = new SolidColorBrush(hue),
                Margin = new Thickness(-2),
                Opacity = GlowRest,
                IsHitTestVisible = false,
                Effect = new BlurEffect { Radius = 26, RenderingBias = RenderingBias.Performance },
                CacheMode = new BitmapCache(),
            };
            Root.Children.Add(_glow);
        }

        _card = new DoorCardBorder
        {
            CornerRadius = new CornerRadius(Radius),
            Background = new SolidColorBrush(spec.Surface),
            BorderBrush = spec.Rim,
            BorderThickness = new Thickness(1.5),
            Cursor = Cursors.Hand,
            Focusable = true,
            FocusVisualStyle = null,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new TransformGroup { Children = { new ScaleTransform(1, 1), new TranslateTransform() } },
        };
        Root.Children.Add(Card);
        // UI Automation reads the door as a button: its title and what it holds.
        AutomationProperties.SetName(Card, spec.Title + ", " + SafeFormat(spec.CountFormat, spec.Count));

        var body = new Grid();
        body.SizeChanged += (_, _) =>
            body.Clip = new RectangleGeometry(new Rect(0, 0, body.ActualWidth, body.ActualHeight), Radius, Radius);

        // --- the reel: two stacked pictures, one in front, drifting; the next one fades over it.
        var reel = new Grid
        {
            IsHitTestVisible = false,
            RenderTransformOrigin = new Point(0.7, 0.5),
            RenderTransform = _zoom,
            // The picture is shown WHOLE (owner, 2026-11-15: "the image is clipped"): the reel is
            // a 16:9 box as tall as the card, seated at the right, and its left edge melts away.
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        // Each picture hugs the card's right edge whatever its shape (square art included) and
        // its own left edge melts away, so no picture ever shows a hard side.
        var melt = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0x00, 0, 0, 0), 0),
                new GradientStop(Color.FromArgb(0xFF, 0, 0, 0), 0.38),
                new GradientStop(Color.FromArgb(0xFF, 0, 0, 0), 1),
            },
        };
        melt.Freeze();
        body.SizeChanged += (_, _) =>
        {
            if (body.ActualHeight > 0)
                reel.Width = Math.Min(body.ActualWidth, body.ActualHeight * 16.0 / 9.0);
        };
        for (int i = 0; i < 2; i++)
        {
            var img = new Image
            {
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Right,
                OpacityMask = melt,
                Opacity = 0,
                RenderTransformOrigin = new Point(1, 0.5),   // grows away from the card's right edge
                RenderTransform = new TransformGroup { Children = { new ScaleTransform(1.0, 1.0), new TranslateTransform() } },
            };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            _layers[i] = img;
            reel.Children.Add(img);
        }
        if (spec.Arts.Count > 0)
        {
            _layers[0].Source = spec.Arts[0];
            _layers[0].Opacity = 1;
        }
        body.Children.Add(reel);

        // The door's hue washed over the picture, brighter on hover.
        _tint = new Border
        {
            IsHitTestVisible = false,
            Opacity = 0.45,
            Background = new LinearGradientBrush
            {
                StartPoint = new Point(0, 1), EndPoint = new Point(1, 0),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0x66, hue.R, hue.G, hue.B), 0),
                    new GradientStop(Color.FromArgb(0x10, hue.R, hue.G, hue.B), 0.55),
                    new GradientStop(Color.FromArgb(0x00, hue.R, hue.G, hue.B), 1),
                },
            },
        };
        body.Children.Add(_tint);

        // The mask: the card's own surface on the left, where the words sit, opening to the
        // picture on the right. Hover thins it so the picture comes up.
        var s = spec.Surface;
        _mask = new Border
        {
            IsHitTestVisible = false,
            Background = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0xF4, s.R, s.G, s.B), 0),
                    new GradientStop(Color.FromArgb(0xCC, s.R, s.G, s.B), 0.30),
                    new GradientStop(Color.FromArgb(0x55, s.R, s.G, s.B), 0.58),
                    new GradientStop(Color.FromArgb(0x10, s.R, s.G, s.B), 1),
                },
            },
        };
        body.Children.Add(_mask);
        body.Children.Add(new Border
        {
            IsHitTestVisible = false,
            Background = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0), EndPoint = new Point(0, 1),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0x00, 0, 0, 0), 0.55),
                    new GradientStop(Color.FromArgb(0x70, 0, 0, 0), 1),
                },
            },
        });

        // --- the words
        var text = new StackPanel { Margin = new Thickness(36, 0, 90, 0), VerticalAlignment = VerticalAlignment.Center };
        // The title shrinks to fit the room the stamp leaves (never grows): "Herramientas" at
        // the window's minimum width would otherwise run under the chevron.
        var titleRow = new DoorTitleRow();
        titleRow.Children.Add(new Viewbox
        {
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.DownOnly,
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = new TextBlock
            {
                Text = spec.Title,
                FontFamily = DisplayFont, FontSize = 60, FontWeight = FontWeights.SemiBold,
                Foreground = _titleBrush,
            },
        });
        if (spec.Stamp is { } stamp)
        {
            _stampScale = new ScaleTransform(1, 1);
            stamp.RenderTransformOrigin = new Point(0.5, 0.5);
            stamp.RenderTransform = new TransformGroup { Children = { _stampScale, new RotateTransform(-6) } };
            stamp.Margin = new Thickness(16, 6, 0, 0);
            stamp.VerticalAlignment = VerticalAlignment.Center;
            titleRow.Children.Add(stamp);
        }
        text.Children.Add(titleRow);

        // The hue bar under the title: it stretches on hover.
        text.Children.Add(new Border
        {
            Width = 64, Height = 5, CornerRadius = new CornerRadius(2.5),
            HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(3, 4, 0, 0),
            Background = new LinearGradientBrush(LauncherDoorCardColors.Lighten(hue, 0.25), Color.FromArgb(0x30, hue.R, hue.G, hue.B), 0),
            RenderTransformOrigin = new Point(0, 0.5),
            RenderTransform = _barScale,
            IsHitTestVisible = false,
        });

        _count = new TextBlock
        {
            FontSize = 16, Margin = new Thickness(3, 12, 0, 0),
            Foreground = spec.CountBrush,
        };
        MotionFx.SetOdometerFormat(_count, OdometerFormat(spec.CountFormat));
        MotionFx.SetOdometerValue(_count, spec.Count);
        _count.Text = SafeFormat(spec.CountFormat, spec.Count);
        text.Children.Add(_count);
        if (spec.Extra is { } extra) text.Children.Add(extra);
        body.Children.Add(text);

        // --- the chevron, on a round plate in the door's hue
        var chevronPlate = new Border
        {
            Width = 50, Height = 50, CornerRadius = new CornerRadius(25),
            Background = new SolidColorBrush(Color.FromArgb(0x38, hue.R, hue.G, hue.B)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x99, hue.R, hue.G, hue.B)),
            BorderThickness = new Thickness(1.5),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 28, 0),
            IsHitTestVisible = false,
            RenderTransform = _chevronSlide,
            Child = new TextBlock
            {
                Text = "", FontFamily = new FontFamily("Segoe MDL2 Assets, Segoe UI Symbol, Segoe UI"),
                FontSize = 20, Foreground = new SolidColorBrush(LauncherDoorCardColors.Lighten(hue, 0.55)),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0, 0, 0),
            },
        };
        body.Children.Add(chevronPlate);
        Card.Child = body;
    }

    private static string OdometerFormat(string format) => format.Replace("{0}", "{0:0}");

    private static string SafeFormat(string format, int n)
    {
        try { return string.Format(format, n); }
        catch { return n.ToString(); }
    }
}

/// <summary>
/// The door card's surface: a Border that UI Automation sees as an invokable button. Mouse,
/// hover and keyboard stay with the window's own handlers; <see cref="Invoked"/> is the same
/// open the click runs.
/// </summary>
internal sealed class DoorCardBorder : Border
{
    internal Action? Invoked { get; set; }

    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);

    private sealed class Peer : FrameworkElementAutomationPeer, IInvokeProvider
    {
        public Peer(DoorCardBorder owner) : base(owner) { }

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Button;
        protected override string GetClassNameCore() => "LauncherDoor";
        protected override bool IsControlElementCore() => true;
        protected override bool IsContentElementCore() => true;

        public override object GetPattern(PatternInterface patternInterface) =>
            patternInterface == PatternInterface.Invoke ? this : base.GetPattern(patternInterface)!;

        void IInvokeProvider.Invoke()
        {
            if (!IsEnabled()) throw new ElementNotEnabledException();
            var owner = (DoorCardBorder)Owner;
            // Like ButtonAutomationPeer: never run the open inside the UIA call itself.
            owner.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => owner.Invoked?.Invoke()));
        }
    }
}

/// <summary>
/// The door title row: the trailing children (the "N new" stamp) keep their own size and the
/// first child (the title) gets what is left, so a long title shrinks instead of clipping.
/// </summary>
internal sealed class DoorTitleRow : Panel
{
    protected override Size MeasureOverride(Size available)
    {
        double trail = 0, height = 0;
        for (int i = 1; i < InternalChildren.Count; i++)
        {
            var child = InternalChildren[i];
            child.Measure(new Size(double.PositiveInfinity, available.Height));
            trail += child.DesiredSize.Width;
            height = Math.Max(height, child.DesiredSize.Height);
        }
        if (InternalChildren.Count == 0) return new Size(trail, height);
        var title = InternalChildren[0];
        double room = double.IsPositiveInfinity(available.Width) ? double.PositiveInfinity : Math.Max(0, available.Width - trail);
        title.Measure(new Size(room, available.Height));
        height = Math.Max(height, title.DesiredSize.Height);
        return new Size(title.DesiredSize.Width + trail, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double x = 0;
        foreach (UIElement child in InternalChildren)
        {
            var d = child.DesiredSize;
            child.Arrange(new Rect(x, Math.Max(0, (finalSize.Height - d.Height) / 2), d.Width, d.Height));
            x += d.Width;
        }
        return finalSize;
    }
}

internal static class LauncherDoorCardColors
{
    internal static Color Lighten(Color c, double amount) => Color.FromRgb(
        (byte)(c.R + (255 - c.R) * amount),
        (byte)(c.G + (255 - c.G) * amount),
        (byte)(c.B + (255 - c.B) * amount));
}
