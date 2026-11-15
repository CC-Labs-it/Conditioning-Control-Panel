using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Safety;
using ConditioningControlPanel.Services.Vault;

namespace ConditioningControlPanel;

/// <summary>
/// The compare face (2026-11-15 redo): two physical tickets on a dark desk. Basic is a cream paper
/// ticket in rose ink, Prime the black card with a foil edge; each has a rubber-stamped price, a
/// numbered perk list (Prime carries on counting from Basic, because it holds Basic), a perforated
/// edge and a tear-off pay stub. The billing switch is a two-position slide lever; the price rolls
/// like an odometer and a round ink stamp lands with the yearly saving.
/// <para>Motion asks <see cref="MotionFx"/> (off = settled at once) and <see cref="GentleVisuals"/>
/// (fades only, rises no faster than the floor). No Effect is animated; ticket shadows are plain
/// offset shapes moved by transforms.</para>
/// </summary>
public sealed partial class VaultGateDialog
{
    private const double OfferWidth = 460;
    private const double CompareWidth = 980;

    private sealed record TicketInk(Brush Stock, Brush Stub, Brush Text, Brush Accent, Brush Muted,
        Brush Rule, Brush Face, Brush Lip, Brush FaceText, Brush? Foil);

    // Hex literals only: static fields in the other part may not be set yet when these run.
    private static readonly Brush Desk = Frozen("#FF141020");
    private static readonly Brush TicketShadow = Frozen("#A005020C");
    private static readonly TicketInk BasicInk = new(
        Lamp("#FFF6EEE0", "#FFE9DDC7"), Frozen("#FFE6D9C2"), Frozen("#FF2B1A33"), Frozen("#FFC8235F"),
        Frozen("#FF7D6878"), Frozen("#66C8235F"), Frozen("#FFC8235F"), Frozen("#FF8A1142"), Frozen("#FFFFF6EC"), null);
    private static readonly TicketInk PrimeInk = new(
        Lamp("#FF261C30", "#FF140F1A"), Frozen("#FF1A1322"), Frozen("#FFF4EBDD"), Frozen("#FFE5C76B"),
        Frozen("#FFA99A84"), Frozen("#66E5C76B"), Frozen("#FFE5C76B"), Frozen("#FF8F6E20"), Frozen("#FF1B1422"), null);

    /// <summary>Shots and tests: build the face settled, no motion.</summary>
    internal bool _still;
    private bool _compareOn, _leaving, _closed, _recentre;
    private Effect? _glow;
    private TranslateTransform? _knobSlide;
    private Border? _knob;
    private TextBlock? _monthLabel, _yearLabel, _yearNote;
    private readonly List<Grid> _tickets = new();
    private readonly List<(int Tier, TicketInk Ink, Grid Amount, TextBlock Struck, TextBlock Sub, StackPanel Sale, FrameworkElement Round)> _stamps = new();

    private bool Still => _still || !MotionFx.AllowTransitions;
    private static bool Gentle => GentleVisuals.On;

    // ============================== the face ==============================

    private void BuildCompare()
    {
        _body.Children.Clear();
        _priceSlots.Clear();
        _stamps.Clear();
        _tickets.Clear();
        _compareOn = true;
        FaceChrome(true);

        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = Heading(Loc.Get("vaultgate_compare_title"));
        title.Margin = new Thickness(0, 0, 20, 0);
        title.FontSize = 25;
        title.VerticalAlignment = VerticalAlignment.Center;
        head.Children.Add(title);
        var lever = Lever();
        Grid.SetColumn(lever, 1);
        head.Children.Add(lever);
        _body.Children.Add(head);

        var row = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(Ticket(1));
        var prime = Ticket(2);
        Grid.SetColumn(prime, 2);
        row.Children.Add(prime);
        _body.Children.Add(row);

        // Always laid out, shown only on yearly, so the switch never resizes the card.
        _yearNote = Para(Loc.Get("vaultgate_yearly_note"), Gold, 12, new Thickness(0, 12, 0, 0));
        _yearNote.Opacity = _yearly ? 1 : 0;
        _body.Children.Add(_yearNote);

        var foot = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        foot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        foot.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var back = Link(Loc.Get("vaultgate_back"));
        back.FontSize = 13;
        back.Margin = new Thickness(0, 0, 28, 0);
        back.MouseLeftButtonUp += (_, _) => LeaveCompare(close: false, () =>
        {
            _compareOn = false;
            FaceChrome(false);
            BuildOffer();
            _ = FillAsyncBits();
            Fade(_body, 1, 200);
        });
        foot.Children.Add(back);
        var fine = Fine(Loc.Get("vaultgate_fine"));
        fine.TextAlignment = TextAlignment.Left;
        fine.Margin = new Thickness(0, 1, 0, 0);
        Grid.SetColumn(fine, 1);
        foot.Children.Add(fine);
        _body.Children.Add(foot);

        PaintLever();
        DealIn();
    }

    /// <summary>The compare face is a wide desk with no pink rim; the other faces keep their card.</summary>
    private void FaceChrome(bool compare)
    {
        _recentre = IsLoaded;
        Width = compare ? CompareWidth : OfferWidth;
        _card.Background = compare ? Desk : CardBg;
        _card.Padding = compare ? new Thickness(30, 20, 30, 14) : new Thickness(22, 20, 22, 18);
        _card.BorderThickness = new Thickness(compare ? 0 : 1.5);
        _card.Effect = compare
            ? new DropShadowEffect { Color = Color.FromRgb(0x06, 0x02, 0x10), BlurRadius = 18, ShadowDepth = 6, Direction = 315, Opacity = 0.7 }
            : _glow;
    }

    /// <summary>A face switch changes the width: keep the card's centre where it was, on screen.</summary>
    private void OnFaceResized(object sender, SizeChangedEventArgs e)
    {
        if (!_recentre || !e.WidthChanged) return;
        _recentre = false;
        var left = Left - (e.NewSize.Width - e.PreviousSize.Width) / 2;
        var top = Top - (e.NewSize.Height - e.PreviousSize.Height) / 2;
        Left = Math.Max(SystemParameters.VirtualScreenLeft,
            Math.Min(left, SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - e.NewSize.Width));
        Top = Math.Max(SystemParameters.VirtualScreenTop,
            Math.Min(top, SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - e.NewSize.Height));
    }

    // ============================== the lever ==============================

    /// <summary>Monthly [ slot ] Yearly: a sunken track and a raised knob that slides. Either word
    /// picks its side, the track flips it, arrows and space work once it has focus.</summary>
    private FrameworkElement Lever()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Focusable = true, Background = Brushes.Transparent };
        _monthLabel = LeverWord(Loc.Get("vaultgate_billing_monthly"), false);
        row.Children.Add(_monthLabel);

        var well = new Grid();
        well.Children.Add(new Border { Height = 8, VerticalAlignment = VerticalAlignment.Top, CornerRadius = new CornerRadius(6, 6, 0, 0), Background = Fall("#A6000000") });
        _knobSlide = new TranslateTransform(_yearly ? 26 : 0, 0);
        _knob = new Border
        {
            Width = 25, Height = 20, CornerRadius = new CornerRadius(4), HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(3, 3, 3, 3), BorderThickness = new Thickness(0, 1, 0, 2.5),
            BorderBrush = Fall("#70FFFFFF", "#90000000"), RenderTransform = _knobSlide,
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Children = { Grip(), Grip(), Grip() },
            },
        };
        well.Children.Add(_knob);
        var track = new Border
        {
            Width = 60, Height = 28, CornerRadius = new CornerRadius(6), Margin = new Thickness(10, 0, 10, 0), Cursor = Cursors.Hand,
            Background = Frozen("#FF0C0815"), BorderBrush = Frozen("#16FFFFFF"), BorderThickness = new Thickness(0, 0, 0, 1), Child = well,
        };
        track.MouseLeftButtonUp += (_, e) => { SetYearly(!_yearly); e.Handled = true; };
        row.Children.Add(track);

        _yearLabel = LeverWord(Loc.Get("vaultgate_billing_yearly"), true);
        row.Children.Add(_yearLabel);
        var off = PlanCatalog.YearlyOffPercent(_plans.Basic);
        if (off > 0)
        {
            var note = new TextBlock
            {
                Text = Loc.GetF("vaultgate_stamp_off", off), Foreground = Gold, FontSize = 12, FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(7, 1, 0, 0), Cursor = Cursors.Hand,
            };
            note.MouseLeftButtonUp += (_, e) => { SetYearly(true); e.Handled = true; };
            row.Children.Add(note);
        }
        row.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Left) SetYearly(false);
            else if (e.Key == Key.Right) SetYearly(true);
            else if (e.Key is Key.Space or Key.Enter) SetYearly(!_yearly);
            else return;
            e.Handled = true;
        };
        return row;
    }

    /// <summary>One ridge on the knob: a dark cut with a lit edge to its left.</summary>
    private static FrameworkElement Grip() => new Border
    {
        Width = 2, Height = 9, Margin = new Thickness(1.5, 0, 1.5, 0), Background = Frozen("#4D000000"),
        BorderBrush = Frozen("#80FFFFFF"), BorderThickness = new Thickness(0.6, 0, 0, 0),
    };

    private TextBlock LeverWord(string text, bool yearly)
    {
        var t = new TextBlock { Text = text, FontSize = 13.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand };
        t.MouseLeftButtonUp += (_, e) => { SetYearly(yearly); e.Handled = true; };
        return t;
    }

    private void PaintLever()
    {
        if (_monthLabel == null || _yearLabel == null || _knob == null || _knobSlide == null) return;
        _monthLabel.Foreground = _yearly ? Dim : Brushes.White;
        _yearLabel.Foreground = _yearly ? Brushes.White : Dim;
        _knob.Background = _yearly ? Lamp("#FFF3DC92", "#FFC9A447") : Lamp("#FFFFF8EC", "#FFD8CBB4");
        var x = _yearly ? 26.0 : 0.0;
        if (Still || Gentle) { _knobSlide.BeginAnimation(TranslateTransform.XProperty, null); _knobSlide.X = x; return; }
        _knobSlide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(x, TimeSpan.FromMilliseconds(160))
        { EasingFunction = new BackEase { Amplitude = 0.3, EasingMode = EasingMode.EaseOut } });
    }

    private void SetYearly(bool yearly)
    {
        if (_yearly == yearly || _leaving) return;
        _yearly = yearly;
        PaintLever();
        foreach (var s in _stamps) PaintStamp(s, animate: true);
        if (_yearNote != null) Fade(_yearNote, yearly ? 1 : 0, 220);
    }

    // ============================== tickets ==============================

    private FrameworkElement Ticket(int tier)
    {
        var lab = tier >= 2;
        var ink = lab ? PrimeInk : BasicInk;
        var name = Loc.Get(lab ? "vaultgate_tier_lab_name" : "vaultgate_tier_vault_name");

        var hover = new TranslateTransform();
        var deal = new TranslateTransform();
        var ticket = new Grid { RenderTransform = new TransformGroup { Children = { hover, deal } } };
        ticket.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        ticket.RowDefinitions.Add(new RowDefinition { Height = new GridLength(20) });
        ticket.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var shadowAt = new TranslateTransform(5, 7);
        var shadow = new Border { CornerRadius = new CornerRadius(8), Background = TicketShadow, RenderTransform = shadowAt };
        Grid.SetRowSpan(shadow, 3);
        ticket.Children.Add(shadow);
        var stock = new Border { CornerRadius = new CornerRadius(8), Background = ink.Stock };
        Grid.SetRowSpan(stock, 3);
        ticket.Children.Add(stock);

        // ---- the main part: admit line, name, stamp, perks ----
        var main = new StackPanel { Margin = new Thickness(26, 16, 26, 4) };
        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var who = new StackPanel();
        who.Children.Add(new TextBlock
        {
            Text = Loc.Get("vaultgate_ticket_admit").ToUpper(System.Globalization.CultureInfo.CurrentUICulture),
            Foreground = ink.Muted, FontSize = 10.5, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap,
        });
        who.Children.Add(new TextBlock
        {
            Text = name, Foreground = ink.Accent, FontSize = 38, FontWeight = FontWeights.Bold,
            FontFamily = ConditioningControlPanel.Helpers.FontPickerHelper.FredokaFamily, Margin = new Thickness(-1, 0, 0, -4),
        });
        who.Children.Add(new TextBlock
        {
            Text = Loc.Get(lab ? "vaultgate_tier_lab_tag" : "vaultgate_tier_vault_tag"), Foreground = ink.Text, Opacity = 0.8,
            FontSize = 15, FontFamily = ConditioningControlPanel.Helpers.FontPickerHelper.FredokaFamily, TextWrapping = TextWrapping.Wrap,
        });
        top.Children.Add(who);
        var sale = new StackPanel();
        var stamp = Stamp(tier, ink, sale);
        Grid.SetColumn(stamp, 1);
        top.Children.Add(stamp);
        main.Children.Add(top);

        main.Children.Add(sale);
        main.Children.Add(new Rectangle { Height = 1.5, Fill = ink.Rule, Margin = new Thickness(0, 10, 0, 9) });
        // Numbered like a programme. Prime keeps counting from Basic: its first row is Basic itself.
        void Perk(string number, string text, Brush brush, double size)
        {
            var line = new Grid { Margin = new Thickness(0, 0, 0, lab ? 9 : 3.5) };
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(lab ? 48 : 30) });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            line.Children.Add(new TextBlock
            {
                Text = number, Foreground = ink.Accent, FontSize = 11.5, FontWeight = FontWeights.Bold,
                FontFamily = new FontFamily("Consolas, Courier New"), Margin = new Thickness(0, size - 11.5, 0, 0),
            });
            var words = new TextBlock { Text = text, Foreground = brush, FontSize = size, TextWrapping = TextWrapping.Wrap };
            Grid.SetColumn(words, 1);
            line.Children.Add(words);
            main.Children.Add(line);
        }
        if (lab) Perk("01-" + BasicPerks.Length.ToString("00"), Loc.Get("vaultgate_prime_head"), ink.Muted, 14);
        else main.Children.Add(new TextBlock
        {
            Text = Loc.Get("vaultgate_basic_head"), Foreground = ink.Muted,
            FontSize = 12, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 5),
        });
        var keys = lab ? PrimePerks : BasicPerks;
        var first = lab ? BasicPerks.Length + 1 : 1;
        for (var i = 0; i < keys.Length; i++) Perk((first + i).ToString("00"), Loc.Get(keys[i]), ink.Text, lab ? 14 : 13);
        ticket.Children.Add(main);

        // ---- the perforation and the tear-off stub ----
        var stubBg = new Border { CornerRadius = new CornerRadius(0, 0, 8, 8), Background = ink.Stub };
        Grid.SetRow(stubBg, 2);
        ticket.Children.Add(stubBg);
        var perf = new Line
        {
            X1 = 0, X2 = 1, Stretch = Stretch.Fill, Stroke = ink.Muted, StrokeThickness = 2, Opacity = 0.55,
            StrokeDashArray = new DoubleCollection { 0.1, 2.6 }, StrokeDashCap = PenLineCap.Round,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(18, 0, 18, 0),
        };
        Grid.SetRow(perf, 1);
        ticket.Children.Add(perf);

        var stub = new Grid { Margin = new Thickness(26, 10, 26, 15) };
        stub.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        stub.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var choose = TicketButton(Loc.GetF("vaultgate_choose", name), ink);
        choose.Click += (_, _) => { OpenPlan(tier, lab ? "compare-lab" : "compare-vault"); Close(); };
        stub.Children.Add(choose);
        var ways = PayWays(tier, lab ? "compare-lab" : "compare-vault", ink.Muted, ink.Text, stacked: true);
        ways.Margin = new Thickness(18, 0, 0, 0);
        ways.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(ways, 1);
        stub.Children.Add(ways);
        Grid.SetRow(stub, 2);
        ticket.Children.Add(stub);

        if (lab)
        {
            var foil = new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(3), BorderBrush = Foil(), IsHitTestVisible = false };
            Grid.SetRowSpan(foil, 3);
            ticket.Children.Add(foil);
        }
        foreach (var side in new[] { HorizontalAlignment.Left, HorizontalAlignment.Right })
        {
            var notch = new Ellipse
            {
                Width = 22, Height = 22, Fill = Desk, HorizontalAlignment = side, VerticalAlignment = VerticalAlignment.Center,
                Margin = side == HorizontalAlignment.Left ? new Thickness(-11, 0, 0, 0) : new Thickness(0, 0, -11, 0), IsHitTestVisible = false,
            };
            Grid.SetRow(notch, 1);
            ticket.Children.Add(notch);
        }

        // Hover lifts the ticket and lengthens its shadow; pressing its button sets it down.
        var pressed = false;
        void Settle()
        {
            var y = pressed ? 2.0 : ticket.IsMouseOver ? -3.0 : 0.0;
            Move(hover, TranslateTransform.YProperty, y, pressed ? 90 : 160);
            Move(shadowAt, TranslateTransform.XProperty, pressed ? 2 : ticket.IsMouseOver ? 7 : 5, 160);
            Move(shadowAt, TranslateTransform.YProperty, pressed ? 3 : ticket.IsMouseOver ? 10 : 7, 160);
        }
        ticket.MouseEnter += (_, _) => Settle();
        ticket.MouseLeave += (_, _) => { pressed = false; Settle(); };
        choose.PreviewMouseLeftButtonDown += (_, _) => { pressed = true; Settle(); };
        choose.PreviewMouseLeftButtonUp += (_, _) => { pressed = false; Settle(); };

        _tickets.Add(ticket);
        PaintStamp(_stamps[^1], animate: false);
        return ticket;
    }

    /// <summary>The raised ink button on the stub: a face over a darker lip. Hover lifts it, a
    /// press sinks it into the lip (on is pressed in).</summary>
    private static Button TicketButton(string text, TicketInk ink)
    {
        const string xaml =
            "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Button'>" +
            "<Grid Background='Transparent'>" +
            "<Border CornerRadius='5' Background='{TemplateBinding BorderBrush}' Margin='0,4,0,0'/>" +
            "<Border x:Name='face' CornerRadius='5' Background='{TemplateBinding Background}' Margin='0,0,0,4'>" +
            "<Grid><Border Height='1' VerticalAlignment='Top' Margin='4,0,4,0' Background='#66FFFFFF'/>" +
            "<ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center' Margin='{TemplateBinding Padding}'/></Grid></Border></Grid>" +
            "<ControlTemplate.Triggers>" +
            "<Trigger Property='IsMouseOver' Value='True'><Setter TargetName='face' Property='RenderTransform'><Setter.Value><TranslateTransform Y='-1'/></Setter.Value></Setter></Trigger>" +
            "<Trigger Property='IsPressed' Value='True'><Setter TargetName='face' Property='RenderTransform'><Setter.Value><TranslateTransform Y='4'/></Setter.Value></Setter></Trigger>" +
            "</ControlTemplate.Triggers></ControlTemplate>";
        return new Button
        {
            Template = (ControlTemplate)XamlReader.Parse(xaml),
            Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center },
            Background = ink.Face, BorderBrush = ink.Lip, Foreground = ink.FaceText, Cursor = Cursors.Hand,
            FontFamily = ConditioningControlPanel.Helpers.FontPickerHelper.FredokaFamily, FontSize = 17, FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(14, 11, 14, 11), VerticalAlignment = VerticalAlignment.Center,
        };
    }

    // ============================== stamps ==============================

    /// <summary>The price as a rubber stamp (a double rule, tilted, a little faded), with the round
    /// yearly-saving stamp parked over its corner until yearly is picked.</summary>
    private FrameworkElement Stamp(int tier, TicketInk ink, StackPanel sale)
    {
        var amount = new Grid { ClipToBounds = true, HorizontalAlignment = HorizontalAlignment.Left };
        var struck = new TextBlock
        {
            Foreground = ink.Muted, FontSize = 15, TextDecorations = TextDecorations.Strikethrough, VerticalAlignment = VerticalAlignment.Bottom,
            FontFamily = ConditioningControlPanel.Helpers.FontPickerHelper.FredokaFamily, Margin = new Thickness(8, 0, 0, 6),
        };
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        line.Children.Add(amount);
        line.Children.Add(struck);
        var sub = new TextBlock { Foreground = ink.Accent, FontSize = 11, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, MaxWidth = 185, Margin = new Thickness(0, -2, 0, 0) };
        var inner = new StackPanel();
        inner.Children.Add(line);
        inner.Children.Add(sub);
        var stamp = new Border
        {
            BorderBrush = ink.Accent, BorderThickness = new Thickness(2.5), CornerRadius = new CornerRadius(3), Padding = new Thickness(2.5),
            Child = new Border { BorderBrush = ink.Accent, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(1.5), Padding = new Thickness(11, 3, 11, 7), Child = inner },
            RenderTransform = new RotateTransform(-4), RenderTransformOrigin = new Point(0.5, 0.5), Opacity = 0.93,
            Margin = new Thickness(0, 14, 6, 0), VerticalAlignment = VerticalAlignment.Top,
        };

        var off = PlanCatalog.YearlyOffPercent(_plans.For(tier));
        var round = new Border
        {
            Width = 84, Height = 84, CornerRadius = new CornerRadius(42), BorderBrush = ink.Accent, BorderThickness = new Thickness(2.5), Padding = new Thickness(2.5),
            Child = new Border
            {
                CornerRadius = new CornerRadius(39), BorderBrush = ink.Accent, BorderThickness = new Thickness(1),
                Child = new TextBlock
                {
                    Text = Loc.GetF("vaultgate_stamp_off", off), Foreground = ink.Accent, FontSize = 13, FontWeight = FontWeights.Bold,
                    FontFamily = ConditioningControlPanel.Helpers.FontPickerHelper.FredokaFamily, TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 2),
                },
            },
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new TransformGroup { Children = { new ScaleTransform(1, 1), new RotateTransform(-11) } },
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(-70, 30, 0, 0),
            IsHitTestVisible = false, Opacity = 0,
        };
        var host = new Grid { VerticalAlignment = VerticalAlignment.Top };
        host.Children.Add(stamp);
        host.Children.Add(round);
        _stamps.Add((tier, ink, amount, struck, sub, sale, round));
        return host;
    }

    private void PaintStamps()
    {
        foreach (var s in _stamps) PaintStamp(s, animate: false);
    }

    /// <summary>Paints one stamp for the current billing. With <paramref name="animate"/> the
    /// amount rolls (up to yearly, down to monthly) and the round stamp thumps in or lifts off.</summary>
    private void PaintStamp((int Tier, TicketInk Ink, Grid Amount, TextBlock Struck, TextBlock Sub, StackPanel Sale, FrameworkElement Round) s, bool animate)
    {
        var price = VaultOffer.PriceFor(s.Tier, _currency, _plans);
        var sale = VaultSale.AppliesTo(_sale, s.Tier, DateTime.UtcNow, _yearly) ? _sale : null;
        var text = VaultOffer.Money(sale != null ? VaultSale.FirstMonthCents(price.MonthlyCents, sale.Percent)
            : _yearly ? price.YearlyCents : price.MonthlyCents, _currency);
        var subText = sale != null ? Loc.Get("vaultgate_sale_first")
            : _yearly ? Loc.GetF("vaultgate_price_year", VaultOffer.YearlyPerMonth(price)) : Loc.GetF("vaultgate_price", VaultOffer.PerDay(price));

        animate &= !Still;
        Roll(s.Amount, text, s.Ink.Accent, animate, up: _yearly);
        s.Struck.Text = sale != null ? VaultOffer.Money(price.MonthlyCents, _currency) : "";
        s.Struck.Visibility = sale != null ? Visibility.Visible : Visibility.Collapsed;
        if (animate && s.Sub.Text != subText)
            Fade(s.Sub, 0, 90, done: () => { s.Sub.Text = subText; Fade(s.Sub, 1, 180); });
        else
        {
            s.Sub.BeginAnimation(OpacityProperty, null);
            s.Sub.Opacity = 1;
            s.Sub.Text = subText;
        }

        s.Sale.Children.Clear();
        if (sale != null)
        {
            s.Sale.Children.Add(Para(Loc.GetF("vaultgate_sale_line", sale.Percent, VaultOffer.Money(price.MonthlyCents, _currency)), s.Ink.Accent, 12, new Thickness(0, 10, 0, 0)));
            if (VaultSale.EndsText(sale) is string ends)
                s.Sale.Children.Add(Para(Loc.GetF("vaultgate_sale_ends", ends), s.Ink.Muted, 11.5, new Thickness(0, 2, 0, 0)));
        }

        var showRound = _yearly && PlanCatalog.YearlyOffPercent(_plans.For(s.Tier)) > 0;
        var scale = (ScaleTransform)((TransformGroup)s.Round.RenderTransform).Children[0];
        if (!animate)
        {
            s.Round.BeginAnimation(OpacityProperty, null);
            s.Round.Opacity = showRound ? 0.9 : 0;
            return;
        }
        if (showRound)
        {
            // the thump: arrives big, lands after the roll, one small settle
            s.Round.Opacity = 0;
            if (!Gentle)
            {
                var land = new DoubleAnimation(1.45, 1, TimeSpan.FromMilliseconds(230))
                { BeginTime = TimeSpan.FromMilliseconds(150), EasingFunction = new BackEase { Amplitude = 0.35, EasingMode = EasingMode.EaseOut } };
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, land);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, land);
            }
            Fade(s.Round, 0.9, 120, delay: 150);
        }
        else Fade(s.Round, 0, 140);
    }

    /// <summary>Odometer roll inside a clipped window: the old figure leaves one way, the new one
    /// arrives from the other with a small overshoot. Gentle: a plain crossfade.</summary>
    private void Roll(Grid window, string text, Brush ink, bool animate, bool up)
    {
        var next = new TextBlock
        {
            Text = text, Foreground = ink, FontSize = 30, FontWeight = FontWeights.Bold,
            FontFamily = ConditioningControlPanel.Helpers.FontPickerHelper.FredokaFamily, RenderTransform = new TranslateTransform(),
        };
        var old = window.Children.OfType<TextBlock>().LastOrDefault();
        foreach (var stale in window.Children.OfType<TextBlock>().Where(t => !ReferenceEquals(t, old)).ToList()) window.Children.Remove(stale);
        window.Children.Add(next);
        if (old == null || !animate)
        {
            if (old != null) window.Children.Remove(old);
            return;
        }
        if (old.Text == text) { window.Children.Remove(next); return; }
        var dir = up ? 1 : -1;
        Fade(old, 0, 130, done: () => window.Children.Remove(old));
        next.Opacity = 0;
        Fade(next, 1, 200, delay: Gentle ? 0 : 40);
        if (Gentle) return;
        ((TranslateTransform)old.RenderTransform).BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(0, -20 * dir, TimeSpan.FromMilliseconds(130)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } });
        ((TranslateTransform)next.RenderTransform).BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(22 * dir, 0, TimeSpan.FromMilliseconds(260))
            { BeginTime = TimeSpan.FromMilliseconds(40), EasingFunction = new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseOut } });
    }

    // ============================== in and out ==============================

    /// <summary>IN: the tickets are dealt onto the desk, Basic then Prime, rising with a little
    /// overshoot. Gentle: they only fade up.</summary>
    private void DealIn()
    {
        for (var i = 0; i < _tickets.Count; i++)
        {
            var t = _tickets[i];
            var deal = (TranslateTransform)((TransformGroup)t.RenderTransform).Children[1];
            t.Opacity = 0;
            Fade(t, 1, 240, delay: 80 * i);
            if (Still || Gentle) continue;
            deal.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(26, 0, TimeSpan.FromMilliseconds(340))
            { BeginTime = TimeSpan.FromMilliseconds(80 * i), EasingFunction = new BackEase { Amplitude = 0.3, EasingMode = EasingMode.EaseOut } });
        }
    }

    /// <summary>OUT: the tickets slide off down the desk and the face fades; then <paramref name="then"/>.
    /// A close fades the whole card. Clicks are refused while it plays.</summary>
    private void LeaveCompare(bool close, Action then)
    {
        if (_leaving) return;
        if (Still) { then(); return; }
        _leaving = true;
        _card.IsHitTestVisible = false;
        for (var i = 0; i < _tickets.Count && !Gentle; i++)
        {
            var deal = (TranslateTransform)((TransformGroup)_tickets[i].RenderTransform).Children[1];
            deal.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, 16, TimeSpan.FromMilliseconds(170))
            { BeginTime = TimeSpan.FromMilliseconds(40 * i), EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } });
        }
        Fade(close ? _card : _body, 0, 190, done: () =>
        {
            _leaving = false;
            _card.IsHitTestVisible = true;
            then();
        });
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_compareOn && !Still && !e.Cancel)
        {
            e.Cancel = true;
            LeaveCompare(close: true, () =>
            {
                _compareOn = false;
                if (_closed) return;
                try { Close(); }
                catch (InvalidOperationException) { } // swallow: the window went while the card faded
            });
            return;
        }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        base.OnClosed(e);
    }

    // ============================== motion bits ==============================

    /// <summary>Opacity tween. Rises respect the gentle floor and length; Off snaps.</summary>
    private void Fade(UIElement el, double to, int ms, int delay = 0, Action? done = null)
    {
        if (Still)
        {
            el.BeginAnimation(OpacityProperty, null);
            el.Opacity = to;
            done?.Invoke();
            return;
        }
        var len = GentleVisuals.FadeMs(ms);
        if (to > el.Opacity) len = GentleVisuals.RiseMs(len);
        var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(len))
        { BeginTime = TimeSpan.FromMilliseconds(delay), EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
        if (done != null) a.Completed += (_, _) => done();
        el.BeginAnimation(OpacityProperty, a);
    }

    /// <summary>A transform tween for hover and press; gentle and Off set it at once.</summary>
    private void Move(TranslateTransform t, DependencyProperty dp, double to, int ms)
    {
        if (Still || Gentle) { t.BeginAnimation(dp, null); t.SetValue(dp, to); return; }
        t.BeginAnimation(dp, new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    /// <summary>Lit from the top-left: a two-stop diagonal, the house lamp.</summary>
    private static Brush Lamp(string lit, string dark)
    {
        var b = new LinearGradientBrush((Color)ColorConverter.ConvertFromString(lit), (Color)ColorConverter.ConvertFromString(dark), new Point(0, 0), new Point(1, 1));
        b.Freeze();
        return b;
    }

    /// <summary>A vertical fall from a colour to clear (or to a second colour).</summary>
    private static Brush Fall(string from, string to = "#00000000")
    {
        var b = new LinearGradientBrush((Color)ColorConverter.ConvertFromString(from), (Color)ColorConverter.ConvertFromString(to), 90);
        b.Freeze();
        return b;
    }

    /// <summary>Gold foil for Prime's edge: bright and dark bands, as foil catches one lamp.</summary>
    private static Brush Foil()
    {
        var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        foreach (var (hex, at) in new[] { ("#FFF6E3A1", 0.0), ("#FFB48C2C", 0.28), ("#FFFFF2C2", 0.47), ("#FF9E7A24", 0.7), ("#FFE7C86E", 1.0) })
            b.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(hex), at));
        b.Freeze();
        return b;
    }

    // ============================== test seam ==============================

    /// <summary>The compare card, settled and detached, at the width it shows at.</summary>
    internal static FrameworkElement CompareCardForShot(bool yearly)
    {
        var d = new VaultGateDialog(null, 1, () => { }) { _still = true, _yearly = yearly };
        d.BuildCompare();
        d.Content = null;
        d._card.Width = CompareWidth - 24; // the window's width less the card's margin
        return d._card;
    }
}
