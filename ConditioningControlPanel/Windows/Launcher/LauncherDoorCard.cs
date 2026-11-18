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
    private const int HoverMs = 180;
    private const int ReelHoldMs = 4600;
    private const int ReelFadeMs = 1100;
    private const double ReelDriftSeconds = 7.5;
    private const int ReelFps = 30;
    private const int EnterMs = 520;
    private const int EnterStaggerMs = 90;
    private const double EnterFromX = 46;
    private const double EnterFromScale = 0.94;
    internal const int OpenMs = 230;
    private const double GlowRest = 0.0;
    private const double GlowHover = 0.55;

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
    private DispatcherTimer? _reelTimer;
    private int _artIndex;
    private int _front;
    private bool _driftLeft;
    private bool _hover;

    internal Grid Root { get; }
    internal Border Card => _card;
    private readonly DoorCardBorder _card;

    /// <summary>What UI Automation's Invoke runs on this door (the window passes its open).</summary>
    internal Action? Invoked { get => _card.Invoked; set => _card.Invoked = value; }
    internal Color Hue => _spec.Hue;
    internal bool ReelRunning => _reelTimer?.IsEnabled == true;

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

    // ------------------------------------------------------------------ poses

    private static bool Animate => MotionFx.AllowTransitions;
    private static bool Lively => MotionFx.AmbientAllowedByLevel;

    private static void To(DependencyObject target, DependencyProperty dp, double to, int ms,
                           IEasingFunction? ease = null, int delayMs = 0, bool fade = false)
    {
        if (target is not IAnimatable anim) return;
        if (!Animate)
        {
            anim.BeginAnimation(dp, null);
            target.SetValue(dp, to);
            return;
        }
        int dur = fade ? GentleVisuals.FadeMs(ms) : ms;
        anim.BeginAnimation(dp, new DoubleAnimation(to, TimeSpan.FromMilliseconds(dur))
        {
            BeginTime = TimeSpan.FromMilliseconds(delayMs),
            EasingFunction = ease ?? new QuadraticEase { EasingMode = EasingMode.EaseOut },
        });
    }

    /// <summary>Hover or keyboard focus. IN: lift, glow, tint, zoom, bar and chevron forward,
    /// title takes the hue. OUT: every one of those back to rest.</summary>
    internal void SetHover(bool on)
    {
        if (_hover == on) return;
        _hover = on;
        try
        {
            MotionFx.HoverLift(Card, on);
            if (_glow != null) To(_glow, UIElement.OpacityProperty, on ? GlowHover : GlowRest, on ? 220 : 320, fade: true);
            To(_tint, UIElement.OpacityProperty, on ? 1.0 : 0.45, HoverMs + 60, fade: true);
            To(_mask, UIElement.OpacityProperty, on ? 0.72 : 1.0, HoverMs + 60, fade: true);
            double z = on ? (Lively ? 1.06 : 1.03) : 1.0;
            To(_zoom, ScaleTransform.ScaleXProperty, z, 420, new CubicEase { EasingMode = EasingMode.EaseOut });
            To(_zoom, ScaleTransform.ScaleYProperty, z, 420, new CubicEase { EasingMode = EasingMode.EaseOut });
            To(_chevronSlide, TranslateTransform.XProperty, on ? 7 : 0, 220,
               on && Lively ? new BackEase { Amplitude = 0.6, EasingMode = EasingMode.EaseOut } : null);
            To(_barScale, ScaleTransform.ScaleXProperty, on ? 2.2 : 1.0, 280,
               on && Lively ? new BackEase { Amplitude = 0.35, EasingMode = EasingMode.EaseOut } : null);
            var title = on ? LauncherDoorCardColors.Lighten(_spec.Hue, 0.55) : _spec.TitleRest;
            if (!Animate) { _titleBrush.BeginAnimation(SolidColorBrush.ColorProperty, null); _titleBrush.Color = title; }
            else _titleBrush.BeginAnimation(SolidColorBrush.ColorProperty,
                new ColorAnimation(title, TimeSpan.FromMilliseconds(GentleVisuals.FadeMs(HoverMs + 40))));
        }
        catch (Exception ex) { Log.Debug(ex, "[Launcher] door hover failed"); }
    }

    /// <summary>The other door is under the cursor: this one steps back a little (and returns).</summary>
    internal void StepBack(bool on)
    {
        try
        {
            To(Root, UIElement.OpacityProperty, on ? 0.78 : 1.0, 240, fade: true);
            To(_rootScale, ScaleTransform.ScaleXProperty, on ? 0.985 : 1.0, 240);
            To(_rootScale, ScaleTransform.ScaleYProperty, on ? 0.985 : 1.0, 240);
        }
        catch (Exception ex) { Log.Debug(ex, "[Launcher] door step back failed"); }
    }

    /// <summary>The entrance: in from the right, a touch small, settling with a little give.
    /// <paramref name="fromOpen"/> is Back: the door that was opened comes down from forward.</summary>
    internal void Enter(int index, bool fromOpen)
    {
        try
        {
            StopPose();
            if (!Animate) { Rest(); return; }
            int delay = EnterStaggerMs * index;
            double fromScale = fromOpen ? 1.07 : EnterFromScale;
            double fromX = fromOpen ? 0 : EnterFromX;
            Root.Opacity = 0;
            _rootScale.ScaleX = _rootScale.ScaleY = fromScale;
            _rootSlide.X = Lively ? fromX : 0;
            _rootSlide.Y = Lively ? 0 : 10;
            IEasingFunction settle = Lively
                ? new BackEase { Amplitude = 0.32, EasingMode = EasingMode.EaseOut }
                : new QuadraticEase { EasingMode = EasingMode.EaseOut };
            To(Root, UIElement.OpacityProperty, 1, 260, null, delay, fade: true);
            To(_rootScale, ScaleTransform.ScaleXProperty, 1.0, EnterMs, settle, delay);
            To(_rootScale, ScaleTransform.ScaleYProperty, 1.0, EnterMs, settle, delay);
            To(_rootSlide, TranslateTransform.XProperty, 0, EnterMs, settle, delay);
            To(_rootSlide, TranslateTransform.YProperty, 0, EnterMs, settle, delay);

            // The count ticks up and the stamp lands once the card has arrived.
            MotionFx.Odometer(_count, 0, _spec.Count, OdometerFormat(_spec.CountFormat), 0.6);
            StampIn(delay + 300);
        }
        catch (Exception ex) { Log.Debug(ex, "[Launcher] door entrance failed"); Rest(); }
    }

    private void StampIn(int delayMs)
    {
        if (_stampScale == null) return;
        if (!Animate) { _stampScale.ScaleX = _stampScale.ScaleY = 1; return; }
        _stampScale.ScaleX = _stampScale.ScaleY = 0;
        var pop = new DoubleAnimationUsingKeyFrames
        {
            BeginTime = TimeSpan.FromMilliseconds(delayMs),
            Duration = TimeSpan.FromMilliseconds(Lively ? 360 : 220),
        };
        if (Lively)
        {
            pop.KeyFrames.Add(new EasingDoubleKeyFrame(1.35, KeyTime.FromPercent(0.45), new QuadraticEase { EasingMode = EasingMode.EaseOut }));
            pop.KeyFrames.Add(new EasingDoubleKeyFrame(0.92, KeyTime.FromPercent(0.72), new QuadraticEase { EasingMode = EasingMode.EaseInOut }));
        }
        pop.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromPercent(1), new QuadraticEase { EasingMode = EasingMode.EaseOut }));
        _stampScale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        _stampScale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
    }

    /// <summary>Opened: this door pushes forward and lets go.</summary>
    internal void PushForward()
    {
        try
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
            To(_rootScale, ScaleTransform.ScaleXProperty, Lively ? 1.08 : 1.02, OpenMs, ease);
            To(_rootScale, ScaleTransform.ScaleYProperty, Lively ? 1.08 : 1.02, OpenMs, ease);
            To(Root, UIElement.OpacityProperty, 0, OpenMs, new QuadraticEase { EasingMode = EasingMode.EaseIn }, 40, fade: true);
            if (_glow != null) To(_glow, UIElement.OpacityProperty, 0.9, 120);
        }
        catch (Exception ex) { Log.Debug(ex, "[Launcher] door push failed"); }
    }

    /// <summary>The other door was opened: this one falls away from it.</summary>
    internal void FallAway(bool downward)
    {
        try
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
            To(_rootScale, ScaleTransform.ScaleXProperty, 0.92, OpenMs - 40, ease);
            To(_rootScale, ScaleTransform.ScaleYProperty, 0.92, OpenMs - 40, ease);
            if (Lively) To(_rootSlide, TranslateTransform.YProperty, downward ? 46 : -46, OpenMs - 40, ease);
            To(Root, UIElement.OpacityProperty, 0, OpenMs - 60, ease, fade: true);
        }
        catch (Exception ex) { Log.Debug(ex, "[Launcher] door fall failed"); }
    }

    /// <summary>No motion: every moving part at rest. Used when transitions are off.</summary>
    internal void Rest()
    {
        StopPose();
        Root.Opacity = 1;
        _rootScale.ScaleX = _rootScale.ScaleY = 1;
        _rootSlide.X = _rootSlide.Y = 0;
        if (_stampScale != null) _stampScale.ScaleX = _stampScale.ScaleY = 1;
        _count.BeginAnimation(MotionFx.OdometerValueProperty, null);
        _count.Text = SafeFormat(_spec.CountFormat, _spec.Count);
    }

    private void StopPose()
    {
        Root.BeginAnimation(UIElement.OpacityProperty, null);
        _rootScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        _rootScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        _rootSlide.BeginAnimation(TranslateTransform.XProperty, null);
        _rootSlide.BeginAnimation(TranslateTransform.YProperty, null);
        _stampScale?.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        _stampScale?.BeginAnimation(ScaleTransform.ScaleYProperty, null);
    }

    /// <summary>Render-suite seam: hold a static pose (hover on, scale, opacity) with no clock.</summary>
    internal void HoldPose(bool hover, double scale, double opacity)
    {
        _hover = hover;
        StopPose();
        Root.Opacity = opacity;
        _rootScale.ScaleX = _rootScale.ScaleY = scale;
        if (_glow != null) _glow.Opacity = hover ? GlowHover : GlowRest;
        _tint.Opacity = hover ? 1.0 : 0.45;
        _mask.Opacity = hover ? 0.72 : 1.0;
        _zoom.ScaleX = _zoom.ScaleY = hover ? 1.06 : 1.0;
        _chevronSlide.X = hover ? 7 : 0;
        _barScale.ScaleX = hover ? 2.2 : 1.0;
        _titleBrush.Color = hover ? LauncherDoorCardColors.Lighten(_spec.Hue, 0.55) : _spec.TitleRest;
        if (Card.RenderTransform is TransformGroup g && g.Children[0] is ScaleTransform lift)
            lift.ScaleX = lift.ScaleY = hover ? MotionFx.HoverLiftScale : 1.0;
    }

    // ------------------------------------------------------------------ the reel

    /// <summary>Starts the drift and the slow cross-fade. Reduced motion keeps the still.</summary>
    internal void StartReel()
    {
        try
        {
            if (_spec.Arts.Count == 0 || !Lively) return;
            Drift(_layers[_front]);
            if (_spec.Arts.Count < 2) return;
            _reelTimer ??= new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(ReelHoldMs) };
            _reelTimer.Tick -= OnReelTick;
            _reelTimer.Tick += OnReelTick;
            _reelTimer.Start();
        }
        catch (Exception ex) { Log.Debug(ex, "[Launcher] door reel start failed"); }
    }

    /// <summary>Every reel clock stops where it is; nothing keeps ticking off screen.</summary>
    internal void StopReel()
    {
        try
        {
            _reelTimer?.Stop();
            foreach (var layer in _layers)
            {
                Freeze(layer, UIElement.OpacityProperty);
                if (layer.RenderTransform is TransformGroup g)
                {
                    Freeze(g.Children[0], ScaleTransform.ScaleXProperty);
                    Freeze(g.Children[0], ScaleTransform.ScaleYProperty);
                    Freeze(g.Children[1], TranslateTransform.XProperty);
                }
            }
        }
        catch (Exception ex) { Log.Debug(ex, "[Launcher] door reel stop failed"); }
    }

    private static void Freeze(DependencyObject d, DependencyProperty dp)
    {
        if (d is not IAnimatable a) return;
        var now = d.GetValue(dp);
        a.BeginAnimation(dp, null);
        d.SetValue(dp, now);
    }

    private void OnReelTick(object? sender, EventArgs e)
    {
        try
        {
            if (!Lively || _spec.Arts.Count < 2) { StopReel(); return; }
            _artIndex = (_artIndex + 1) % _spec.Arts.Count;
            var leaving = _layers[_front];
            _front = 1 - _front;
            var arriving = _layers[_front];
            arriving.Source = _spec.Arts[_artIndex];
            Panel.SetZIndex(arriving, 1);
            Panel.SetZIndex(leaving, 0);
            Drift(arriving);
            var fadeMs = GentleVisuals.FadeMs(ReelFadeMs);
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(fadeMs))
            {
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            };
            Timeline.SetDesiredFrameRate(fadeIn, ReelFps);
            fadeIn.Completed += (_, _) =>
            {
                try
                {
                    if (!ReferenceEquals(_layers[_front], arriving)) return;
                    Freeze(leaving, UIElement.OpacityProperty);
                    leaving.Opacity = 0;
                    if (leaving.RenderTransform is TransformGroup g)
                    {
                        g.Children[0].BeginAnimation(ScaleTransform.ScaleXProperty, null);
                        g.Children[0].BeginAnimation(ScaleTransform.ScaleYProperty, null);
                        g.Children[1].BeginAnimation(TranslateTransform.XProperty, null);
                    }
                }
                catch { }
            };
            arriving.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        }
        catch (Exception ex) { Log.Debug(ex, "[Launcher] door reel tick failed"); }
    }

    /// <summary>Ken Burns: a slow push in with a sideways pan, alternating direction.</summary>
    private void Drift(Image layer)
    {
        if (layer.RenderTransform is not TransformGroup g) return;
        var scale = (ScaleTransform)g.Children[0];
        var pan = (TranslateTransform)g.Children[1];
        _driftLeft = !_driftLeft;
        var span = TimeSpan.FromSeconds(ReelDriftSeconds + ReelHoldMs / 1000.0);
        // A gentle push only: the picture starts whole and never loses more than its outer edge.
        var zoom = new DoubleAnimation(1.0, 1.05, span);
        Timeline.SetDesiredFrameRate(zoom, ReelFps);
        // Only ever toward the card's right edge (clipped there), so no gap opens beside the picture.
        var slide = new DoubleAnimation(_driftLeft ? 5 : 0, _driftLeft ? 0 : 5, span);
        Timeline.SetDesiredFrameRate(slide, ReelFps);
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, zoom);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, zoom);
        pan.BeginAnimation(TranslateTransform.XProperty, slide);
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
