using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Metadata;
using Avalonia.Styling;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Super;

namespace ConditioningControlPanel.Avalonia.Controls.Super;

/// <summary>
/// The gold V2 box at the foot of a feature page, ported from WPF Controls/SuperBox.cs. The BASIC
/// sign (free accounts), the star (the only control since WPF folded the three-way pick away at
/// :226), the effect's name with its v2 tag and help, one plain line, the weekly try corner, then
/// <see cref="Body"/>. LIT when <see cref="SuperAccess.IsUnlocked"/>, DIM otherwise (WPF Paint :319).
/// Not ported: the sign's ambient sheen (WPF :340-388) and the folded picker.
/// </summary>
public sealed class SuperBox : Border
{
    private static readonly Color Gold = Color.FromRgb(0xFF, 0xCF, 0x6B);
    private static readonly Color Mint = Color.FromRgb(0x5F, 0xFF, 0xD0);
    private const double RingR = 9;
    private const double DimOpacity = 0.5;

    public static readonly StyledProperty<SuperEffect> EffectProperty =
        AvaloniaProperty.Register<SuperBox, SuperEffect>(nameof(Effect), SuperEffect.FlickerDeck);

    public SuperEffect Effect { get => GetValue(EffectProperty); set => SetValue(EffectProperty, value); }

    public static readonly StyledProperty<object?> BodyProperty =
        AvaloniaProperty.Register<SuperBox, object?>(nameof(Body));

    /// <summary>The effect's own rows under the switch. Never dimmed by the box.</summary>
    [Content]
    public object? Body { get => GetValue(BodyProperty); set => SetValue(BodyProperty, value); }

    private readonly Border _v2Tag;
    private readonly Border _signHost = new() { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _title = new() { FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = new SolidColorBrush(Gold), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _twist = new() { FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Opacity = 0.7, Foreground = Brushes.White, Margin = new Thickness(0, 1, 0, 0) };
    private readonly Border _stripe = new()
    {
        Width = 3, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 10),
        CornerRadius = new CornerRadius(0, 2, 2, 0), IsHitTestVisible = false, Background = new SolidColorBrush(Gold),
    };
    private readonly TextBlock _baseOff = new() { FontSize = 11, FontStyle = FontStyle.Italic, Opacity = 0.6, Foreground = Brushes.White, Margin = new Thickness(0, 3, 0, 0) };
    private readonly Grid _head = new() { Margin = new Thickness(0, 2, 0, 0), MinHeight = 22 };
    private readonly Button _help = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(7, 0, 0, 0) };
    private SuperEffect? _helpFor;
    private readonly Button _star;
    private readonly IconGlyph _starGlyph = new() { Kind = IconKind.Star, Variant = IconVariant.Filled, Size = 24 };
    private static readonly IBrush StarOn = new SolidColorBrush(Gold);
    private static readonly IBrush StarOff = new SolidColorBrush(Color.FromArgb(0x59, 0xFF, 0xFF, 0xFF));
    private readonly StackPanel _corner = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
    private readonly ContentPresenter _body = new();
    private readonly Arc _ringArc = new()
    {
        Width = 2 * RingR, Height = 2 * RingR, StartAngle = -90, Stroke = new SolidColorBrush(Mint), StrokeThickness = 2,
        StrokeLineCap = PenLineCap.Round, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly TextBlock _ringText = new() { FontSize = 8.5, FontWeight = FontWeight.Bold, Foreground = new SolidColorBrush(Mint), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly Grid _ring = new() { Width = 24, Height = 24, VerticalAlignment = VerticalAlignment.Center };
    private DispatcherTimer? _ringTimer;
    private bool _painted;

    public SuperBox()
    {
        // The page's own option card chrome (SettingCardStyle), in gold (WPF :118).
        CornerRadius = new CornerRadius(12);
        Margin = new Thickness(0, 4, 0, 10);
        BorderThickness = new Thickness(2);

        _v2Tag = new Border
        {
            Padding = new Thickness(6, 1, 7, 2), CornerRadius = new CornerRadius(7),
            Background = new SolidColorBrush(Color.FromArgb(0xD9, 0x1A, 0x1A, 0x2E)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xE0, 0x8A)), BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0), IsHitTestVisible = false,
            Child = new TextBlock { Text = Loc.Get("badge_v2"), FontSize = 9, FontWeight = FontWeight.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xE0, 0x8A)) },
        };
        _signHost.Child = new TierBadge { Tier = 1, MaxWidthOverride = 64, HorizontalAlignment = HorizontalAlignment.Right };
        _head.Children.Add(_signHost);

        _star = new Button
        {
            Cursor = new Cursor(StandardCursorType.Hand), Template = ChipTemplate(),
            Background = Brushes.Transparent, BorderBrush = Brushes.Transparent,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(-6, 0, 4, 0), Content = _starGlyph,
        };
        _star.Click += (_, _) => OnStarPressed();
        ToolTip.SetShowOnDisabled(_star, true);

        _ring.Children.Add(new Ellipse { Width = 2 * RingR + 2, Height = 2 * RingR + 2, Stroke = new SolidColorBrush(Color.FromRgb(0x33, 0x26, 0x4F)), StrokeThickness = 2, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        _ring.Children.Add(_ringArc);
        _ring.Children.Add(_ringText);

        if (Application.Current?.TryFindResource("HelpButtonStyle", out var theme) == true && theme is ControlTheme ct) _help.Theme = ct;
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Children = { _title, _v2Tag, _help } };
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 2, 0, 2), Children = { titleRow, _twist } };

        var row = new Grid { MinHeight = 36, ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        Grid.SetColumn(text, 1);
        Grid.SetColumn(_corner, 2);
        row.Children.Add(_star);
        row.Children.Add(text);
        row.Children.Add(_corner);

        var root = new StackPanel { Margin = new Thickness(16, 4, 16, 6), Children = { _head, row, _baseOff, _body } };
        Child = new Grid { Children = { _stripe, root } };

        Loaded += (_, _) =>
        {
            SuperAccess.HookTierEvents();
            SuperAccess.HookBaseEvents();
            SuperAccess.Changed += OnAccessChanged;
            SuperPreview.StateChanged += OnPreviewChanged;
            Refresh();
        };
        Unloaded += (_, _) =>
        {
            SuperAccess.Changed -= OnAccessChanged;
            SuperPreview.StateChanged -= OnPreviewChanged;
            StopRing();
        };

        OnBodyChanged(null);
        Paint(lit: false);
    }

    /// <summary>Effects with a running lane on this head; each host lane adds its own. For any other effect the
    /// star and the weekly try stay off, so no free try is spent on nothing. Creep: the Steam fog on the
    /// tint windows (Views/Overlays/SteamOverlay); Afterglow: the line (Views/Overlays/AfterglowLineOverlay); FlickerDeck:
    /// the cards on the flash windows (Views/Overlays/FlashOverlay.Deck); InnerBloom (Froth): the ambient bubbles (Views/Overlays/BubbleOverlay);
    /// Vortex: the Whirlpool on the spiral windows (Views/Overlays/WhirlpoolOverlay).</summary>
    internal static readonly HashSet<SuperEffect> HostedOnThisHead = new() { SuperEffect.Creep, SuperEffect.Afterglow, SuperEffect.FlickerDeck, SuperEffect.InnerBloom, SuperEffect.Vortex };

    /// <summary>True when the star draws on: hosted, unlocked and switched on.</summary>
    public bool StarLit { get; private set; }

    /// <summary>True when the box draws lit (tier or a running try), false when dim.</summary>
    public bool IsLit { get; private set; }

    internal Button Star => _star;
    internal Panel Corner => _corner;
    internal string? RingText => _corner.Children.Contains(_ring) ? _ringText.Text : null;

    /// <summary>WPF :268: a locked tier shakes the sign and gets the tier refusal (SuperDoor), others flip.</summary>
    internal void OnStarPressed()
    {
        if (!HostedOnThisHead.Contains(Effect)) return;
        if (!SuperDoor.Press(Effect)) Shake(_signHost);
        Refresh();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == EffectProperty) Refresh();
        else if (change.Property == BodyProperty) OnBodyChanged(change.NewValue);
    }

    private void OnBodyChanged(object? body)
    {
        _body.Content = body;
        _body.IsVisible = body != null;
    }

    private void OnAccessChanged(SuperEffect e) { if (e == Effect) Ui(Refresh); }
    private void OnPreviewChanged() => Ui(Refresh);

    private static void Ui(Action a)
    {
        if (Dispatcher.UIThread.CheckAccess()) a();
        else Dispatcher.UIThread.Post(a);
    }

    /// <summary>LIT or DIM (WPF :319). A real change on a shown box eases (SuperChromeJuice.ChangeMs); the
    /// first paint and any offscreen paint land at once.</summary>
    private void Paint(bool lit)
    {
        int ms = IsLoaded && _painted && lit != IsLit ? SuperChromeJuice.ChangeMs(AmbientFxCanvas.Env.Level) : 0;
        _painted = true;
        IsLit = lit;
        Ease(this, ms, BorderBrushProperty, BackgroundProperty);
        BorderBrush = new SolidColorBrush(Color.FromArgb(lit ? (byte)0xB3 : (byte)0x4D, Gold.R, Gold.G, Gold.B));
        Background = new SolidColorBrush(Color.FromArgb(lit ? (byte)0x1C : (byte)0x0C, Gold.R, Gold.G, Gold.B));
        double o = lit ? 1.0 : DimOpacity;
        Fade(_title, o, ms);
        Fade(_twist, 0.7 * o, ms);
        Fade(_v2Tag, o, ms);
        Fade(_stripe, lit ? 0.9 : 0.35, ms);
        // The supporter sign is what a free player is asked for, so it stays readable.
        Fade(_signHost, lit ? 0.85 : 0.9, ms);
    }

    private static void Fade(Control c, double to, int ms)
    {
        Ease(c, ms, OpacityProperty);
        c.Opacity = to;
    }

    private static void Ease(Animatable target, int ms, params AvaloniaProperty[] props)
    {
        if (ms <= 0) { target.Transitions = null; return; }
        var all = new Transitions();
        var d = TimeSpan.FromMilliseconds(ms);
        foreach (var p in props)
            all.Add(p == OpacityProperty
                ? new DoubleTransition { Property = p, Duration = d, Easing = new CubicEaseOut() }
                : new BrushTransition { Property = p, Duration = d, Easing = new CubicEaseOut() });
        target.Transitions = all;
    }

    /// <summary>WPF SuperChrome.Shake: 400 ms, -4/+4/-3/+2 px with a 1.5 degree rock, halved when
    /// reduced, skipped when motion is off.</summary>
    private static void Shake(Control target)
    {
        var level = AmbientFxCanvas.Env.Level;
        if (level == MotionLevel.Off) return;
        double h = level == MotionLevel.Reduced ? 0.5 : 1.0;
        target.RenderTransformOrigin = RelativePoint.Center;
        if (target.RenderTransform is not TransformGroup)
            target.RenderTransform = new TransformGroup { Children = { new RotateTransform(), new TranslateTransform() } };
        var shake = new Animation { Duration = TimeSpan.FromMilliseconds(400) };
        foreach (var (t, x, a) in new[] { (0.2, -4.0, -1.5), (0.4, 4.0, 1.5), (0.6, -3.0, 0.0), (0.8, 2.0, 0.0), (1.0, 0.0, 0.0) })
            shake.Children.Add(new KeyFrame
            {
                Cue = new Cue(t),
                Setters = { new Setter(TranslateTransform.XProperty, x * h), new Setter(RotateTransform.AngleProperty, a * h) },
            });
        _ = shake.RunAsync(target);
    }

    /// <summary>Repaint every part from <see cref="SuperAccess"/> and <see cref="SuperPreview"/> (WPF :390-452).</summary>
    public void Refresh()
    {
        var effect = Effect;
        string name = Loc.Get("super_name_" + effect.ToString().ToLowerInvariant());
        _title.Text = name;
        if (_helpFor != effect)
        {
            _helpFor = effect;
            HelpPopover.Attach(_help, SuperDoor.Help(effect), new IconEntry(IconKind.Star, IconVariant.Filled, IconBrushes.Gold));
        }
        _twist.Text = SuperDoor.Twist(effect);

        bool paid = TierGate.HasPremium;
        bool trying = SuperPreview.Trying == effect;
        bool unlocked = SuperAccess.IsUnlocked(effect);
        bool thisWeek = !paid && SuperPreview.ThisWeek == effect;
        bool baseOn = SuperAccess.IsBaseOn(effect);
        bool hosted = HostedOnThisHead.Contains(effect);

        Paint(lit: unlocked);
        StarLit = hosted && unlocked && SuperAccess.IsSwitchedOn(effect);
        _star.IsEnabled = hosted;
        ToolTip.SetTip(_star, hosted ? null : Loc.Get("label_coming_soon"));
        _starGlyph.Foreground = StarLit ? StarOn : StarOff;
        AutomationProperties.SetName(_star, name);
        _head.IsVisible = !paid;
        _baseOff.Text = Loc.Get("super_base_off");
        _baseOff.IsVisible = !baseOn && (trying || (unlocked && SuperAccess.IsSwitchedOn(effect)));

        _corner.Children.Clear();
        StopRing();
        if (paid && !trying) return;

        if (trying)
        {
            _corner.Children.Add(_ring);
            StartRing();
        }
        else if (hosted && thisWeek && SuperPreview.CanTry(effect) && baseOn)
        {
            // WPF :436 shakes the folded (collapsed) picker on a refusal, which draws nothing.
            _corner.Children.Add(MakeChip(Loc.Get("super_try"), Mint, () => SuperPreview.TryStart(effect)));
        }
        else
        {
            if (thisWeek && SuperPreview.UsedThisWeek)
                _corner.Children.Add(new TextBlock
                {
                    Text = Loc.Get("super_back_monday"), FontSize = 10.5, Opacity = 0.65, Foreground = Brushes.White,
                    VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0),
                });
            _corner.Children.Add(MakeChip(Loc.Get("super_get_basic"), Gold, () =>
                TierGate.DemandPremium(Loc.GetF("super_switch_name", name))));
        }
    }

    /// <summary>A small outlined chip in the box's own colours. The TextBlock keeps '_' out of access keys.</summary>
    private static Button MakeChip(string text, Color ink, Action click)
    {
        var b = new Button
        {
            Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center, Template = ChipTemplate(),
            Background = new SolidColorBrush(Color.FromArgb(0x24, ink.R, ink.G, ink.B)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0xB3, ink.R, ink.G, ink.B)),
            Foreground = new SolidColorBrush(ink),
            Content = new TextBlock { Text = text, FontSize = 10.5, FontWeight = FontWeight.SemiBold },
        };
        b.Click += (_, _) => click();
        return b;
    }

    private static IControlTemplate ChipTemplate() => new FuncControlTemplate<Button>((b, _) => new Border
    {
        CornerRadius = new CornerRadius(999), Padding = new Thickness(9, 2, 9, 3), BorderThickness = new Thickness(1),
        [!Border.BackgroundProperty] = b[!Button.BackgroundProperty],
        [!Border.BorderBrushProperty] = b[!Button.BorderBrushProperty],
        Child = new ContentPresenter
        {
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            [!ContentPresenter.ContentProperty] = b[!ContentControl.ContentProperty],
        },
    });

    private void StartRing()
    {
        UpdateRing();
        _ringTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _ringTimer.Tick += (_, _) => UpdateRing();
        _ringTimer.Start();
    }

    private void StopRing()
    {
        _ringTimer?.Stop();
        _ringTimer = null;
    }

    /// <summary>The ring empties as the try runs out (WPF :523).</summary>
    private void UpdateRing()
    {
        double left = SuperPreview.SecondsLeft;
        _ringArc.SweepAngle = 360 * left / SuperPreviewRule.TrySeconds;
        _ringText.Text = Loc.GetF("super_try_left", (int)Math.Ceiling(left));
    }
}
