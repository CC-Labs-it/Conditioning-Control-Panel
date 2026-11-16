using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Animation;
using Avalonia.Collections;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Safety;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Vault;

namespace ConditioningControlPanel.Avalonia.Views.Dialogs;

// WPF VaultGateDialog.Compare.cs:65-140, 245-392: desk, ticket stock and tear-off stubs.
public sealed partial class VaultGateDialog
{
    private bool _comparing, _resizePending, _leaving;
    private double _resizeOldHeight, _resizeTargetWidth;
    private readonly List<(int Tier, StackPanel Price, Border Saving)> _comparePrices = new();
    private readonly List<Border> _compareTickets = new();
    private TextBlock? _monthWord, _yearWord, _leverSaving, _yearNote;
    private Border? _knob;
    private TranslateTransform? _knobSlide;
    private readonly Dictionary<Border, TranslateTransform> _ticketMoves = new();
    private Button? _leverBadge;
    private CancellationTokenSource? _billingMotion;
    private static MotionLevel CompareMotion => AmbientFxCanvas.Env.Level;

    // WPF Compare.cs:140-151. Position and working area are physical pixels, sizes are DIPs.
    internal static PixelPoint Recenter(PixelPoint at, Size before, Size after, PixelRect area, double scale)
    {
        scale = scale > 0 ? scale : 1;
        var x = at.X - (after.Width - before.Width) * scale / 2;
        var y = at.Y - (after.Height - before.Height) * scale / 2;
        var maxX = Math.Max(area.X, area.Right - after.Width * scale);
        var maxY = Math.Max(area.Y, area.Bottom - after.Height * scale);
        return new PixelPoint((int)Math.Round(Math.Clamp(x, area.X, maxX)), (int)Math.Round(Math.Clamp(y, area.Y, maxY)));
    }

    private void CompareSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (!_resizePending || Math.Abs(e.NewSize.Width - _resizeTargetWidth) > 1) return;
        _resizePending = false;
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen != null)
            Position = Recenter(Position, new Size(_resizeTargetWidth, _resizeOldHeight), e.NewSize, screen.WorkingArea, screen.Scaling);
    }

    private void FaceChrome(bool compare)
    {
        var target = compare ? 980 : 460;
        if (IsVisible && Width != target)
        {
            var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
            if (screen != null)
            {
                var before = new Size(Bounds.Width > 0 ? Bounds.Width : Width, Bounds.Height);
                Position = Recenter(Position, before, new Size(target, before.Height), screen.WorkingArea, screen.Scaling);
                _resizeOldHeight = before.Height;
                _resizeTargetWidth = target;
                _resizePending = true;
            }
        }
        MinWidth = compare ? 980 : 0;
        Width = target;
        _card.Background = compare ? B("#141020") : CardBg;
        _card.Padding = compare ? new Thickness(30, 20, 30, 14) : new Thickness(22, 20, 22, 18);
        _card.BorderThickness = new Thickness(compare ? 0 : 1.5);
    }

    private void BuildCompare()
    {
        FaceChrome(true);
        _comparing = true;
        _comparePrices.Clear();
        _compareTickets.Clear();
        _ticketMoves.Clear();
        _body.Children.Clear();
        _priceSlots.Clear();
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var title = Heading(Loc.Get("vaultgate_compare_title"));
        title.FontSize = 25;
        title.Margin = new Thickness(0, 0, 20, 0);
        head.Children.Add(title);
        var lever = BillingSwitch();
        Grid.SetColumn(lever, 1);
        head.Children.Add(lever);
        _body.Children.Add(head);
        var row = new Grid { Margin = new Thickness(0, 14, 0, 0), ColumnDefinitions = new ColumnDefinitions("*,26,*") };
        row.Children.Add(Ticket(1));
        var prime = Ticket(2);
        Grid.SetColumn(prime, 2);
        row.Children.Add(prime);
        _body.Children.Add(row);
        _yearNote = Para(Loc.Get("vaultgate_yearly_note"), Gold, 12, new Thickness(0, 12, 0, 0));
        _body.Children.Add(_yearNote);
        var foot = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(0, 8, 0, 0) };
        var back = Link(Loc.Get("vaultgate_back"));
        back.Margin = new Thickness(0, 0, 28, 0);
        back.Click += (_, _) => LeaveCompare();
        foot.Children.Add(back);
        var fine = Fine(Loc.Get("vaultgate_fine"));
        fine.Margin = new Thickness(0);
        fine.TextAlignment = TextAlignment.Left;
        Grid.SetColumn(fine, 1);
        foot.Children.Add(fine);
        _body.Children.Add(foot);
        PaintCompare(false);
        DealTickets();
    }

    private Control BillingSwitch()
    {
        var row = new StackPanel { Name = "BillingLever", Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Focusable = true };
        row.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Left) SetBilling(false);
            else if (e.Key == Key.Right) SetBilling(true);
            else if (e.Key is Key.Space or Key.Enter) SetBilling(!_yearly);
            else return;
            e.Handled = true;
        }, RoutingStrategies.Tunnel);
        row.AddHandler(KeyUpEvent, (_, e) => { if (e.Key is Key.Space or Key.Enter) e.Handled = true; }, RoutingStrategies.Tunnel);
        foreach (var yearly in new[] { false, true })
        {
            var wordText = new TextBlock { Text = Loc.Get(yearly ? "vaultgate_billing_yearly" : "vaultgate_billing_monthly"), FontSize = 13 };
            if (yearly) _yearWord = wordText; else _monthWord = wordText;
            var word = Bare(wordText);
            word.Click += (_, _) => SetBilling(yearly);
            row.Children.Add(word);
            if (!yearly)
            {
                _knobSlide = new TranslateTransform();
                _knob = new Border { Width = 25, Height = 20, CornerRadius = new CornerRadius(4), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(3), RenderTransform = _knobSlide };
                var track = Bare(new Border { Width = 60, Height = 28, CornerRadius = new CornerRadius(6), Background = B("#0C0815"), Child = _knob });
                track.Margin = new Thickness(10, 0);
                track.Click += (_, _) => SetBilling(!_yearly);
                row.Children.Add(track);
            }
        }
        _leverSaving = new TextBlock { Name = "LeverSaving", Foreground = Gold, FontSize = 12, Margin = new Thickness(7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        _leverBadge = Bare(_leverSaving);
        _leverBadge.Click += (_, _) => SetBilling(true);
        row.Children.Add(_leverBadge);
        return row;
    }

    private void SetBilling(bool yearly)
    {
        if (_yearly == yearly) return;
        _yearly = yearly;
        PaintCompare(true);
    }

    // WPF Compare.cs:194, 231-242, 440-507. Refresh from the same plan snapshot as prices.
    private void PaintCompare(bool animate)
    {
        if (!_comparing || _knob == null || _monthWord == null || _yearWord == null || _leverSaving == null || _yearNote == null) return;
        _billingMotion?.Cancel();
        _billingMotion?.Dispose();
        _billingMotion = new CancellationTokenSource();
        var token = _billingMotion.Token;
        _monthWord.Foreground = _yearly ? Dim : Brushes.White;
        _yearWord.Foreground = _yearly ? Brushes.White : Dim;
        var basicOff = PlanCatalog.YearlyOffPercent(_plans.Basic);
        _leverSaving.Text = basicOff > 0 ? Loc.GetF("vaultgate_stamp_off", basicOff) : "";
        if (_leverBadge != null) _leverBadge.IsVisible = basicOff > 0;
        _knob.Background = _yearly ? Gold : B("#FFF8EC");
        var slide = _knobSlide!;
        var oldX = slide.X;
        slide.X = _yearly ? 26 : 0;
        if (animate && CompareMotion == MotionLevel.Full) _ = Tween(_knob, TranslateTransform.XProperty, oldX, slide.X, 160, token);
        _yearNote.Opacity = _yearly ? 1 : 0;
        for (var i = 0; i < _comparePrices.Count; i++)
        {
            var (tier, price, saving) = _comparePrices[i];
            var off = PlanCatalog.YearlyOffPercent(_plans.For(tier));
            ((TextBlock)saving.Child!).Text = off > 0 ? Loc.GetF("vaultgate_stamp_off", off) : "";
            saving.Opacity = _yearly && off > 0 ? 0.9 : 0;
            var slotIndex = _priceSlots.FindIndex(s => ReferenceEquals(s.Slot, price));
            if (slotIndex >= 0) _priceSlots[slotIndex] = (price, tier, true, _yearly, false);
            PaintPrice(price, tier, true, _yearly, false);
            if (animate && CompareMotion != MotionLevel.Off)
            {
                _ = Tween(price, OpacityProperty, 0, 1, GentleVisuals.RiseMs(GentleVisuals.FadeMs(200)), token);
                if (CompareMotion == MotionLevel.Full)
                    _ = Tween(price, TranslateTransform.YProperty, _yearly ? 22 : -22, 0, 260, token);
            }
        }
    }

    private Control Ticket(int tier)
    {
        var lab = tier == 2;
        var stock = B(lab ? "#261C30" : "#F6EEE0");
        var stubInk = B(lab ? "#1A1322" : "#E6D9C2");
        var text = B(lab ? "#F4EBDD" : "#2B1A33");
        var accent = B(lab ? "#E5C76B" : "#C8235F");
        var muted = B(lab ? "#A99A84" : "#7D6878");
        var name = Loc.Get(lab ? "vaultgate_tier_lab_name" : "vaultgate_tier_vault_name");
        var main = new StackPanel { Margin = new Thickness(26, 16, 26, 8) };
        var top = new Grid { Name = "TicketHeading", ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var who = new StackPanel();
        who.Children.Add(new TextBlock { Text = Loc.Get("vaultgate_ticket_admit").ToUpper(System.Globalization.CultureInfo.CurrentUICulture), Foreground = muted, FontSize = 10.5, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap });
        who.Children.Add(new TextBlock { Text = name, Foreground = accent, FontSize = 32, FontWeight = FontWeight.Bold });
        who.Children.Add(Para(Loc.Get(lab ? "vaultgate_tier_lab_tag" : "vaultgate_tier_vault_tag"), text, 15));
        top.Children.Add(who);
        var price = (StackPanel)Price(tier, compact: true, yearly: _yearly);
        price.Tag = text;
        price.RenderTransform = new TranslateTransform();
        PaintPrice(price, tier, true, _yearly, false);
        var priceRow = new Grid { Name = "TicketPrice", ColumnDefinitions = new ColumnDefinitions("*,Auto"), MaxWidth = 190, VerticalAlignment = VerticalAlignment.Top };
        priceRow.Children.Add(price);
        var saving = new Border { Name = lab ? "PrimeSaving" : "BasicSaving", BorderBrush = accent, BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(30), Padding = new Thickness(6), IsHitTestVisible = false, Child = new TextBlock { Foreground = accent, FontWeight = FontWeight.Bold, FontSize = 12, TextWrapping = TextWrapping.Wrap } };
        Grid.SetColumn(saving, 1);
        priceRow.Children.Add(saving);
        _comparePrices.Add((tier, price, saving));
        Grid.SetColumn(priceRow, 1);
        top.Children.Add(priceRow);
        main.Children.Add(top);
        main.Children.Add(new Border { Height = 1, Background = accent, Opacity = 0.4, Margin = new Thickness(0, 10, 0, 9) });
        void Perk(string number, string words, IBrush ink)
        {
            var line = new Grid { ColumnDefinitions = new ColumnDefinitions("48,*"), Margin = new Thickness(0, 0, 0, lab ? 6 : 3) };
            line.Children.Add(new TextBlock { Text = number, Foreground = accent, FontWeight = FontWeight.Bold, FontSize = 11.5 });
            var value = new TextBlock { Text = words, Foreground = ink, FontSize = lab ? 14 : 13, TextWrapping = TextWrapping.Wrap };
            Grid.SetColumn(value, 1);
            line.Children.Add(value);
            main.Children.Add(line);
        }
        if (lab) Perk("01-" + BasicPerks.Length.ToString("00"), Loc.Get("vaultgate_prime_head"), muted);
        else main.Children.Add(Para(Loc.Get("vaultgate_basic_head"), muted, 12, new Thickness(0, 0, 0, 5)));
        var perks = lab ? PrimePerks : BasicPerks;
        for (var i = 0; i < perks.Length; i++) Perk((i + (lab ? BasicPerks.Length + 1 : 1)).ToString("00"), Loc.Get(perks[i]), text);
        var stub = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(26, 10, 26, 15) };
        var lip = B(lab ? "#8F6E20" : "#8A1142");
        var face = new Border { Name = "TicketFace", Background = accent, CornerRadius = new CornerRadius(5), Margin = new Thickness(0, 0, 0, 4), Padding = new Thickness(14, 11),
            Child = new TextBlock { Text = Loc.GetF("vaultgate_choose", name), Foreground = B(lab ? "#1B1422" : "#FFF6EC"), FontSize = 17, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center } };
        var faceMove = new TranslateTransform();
        face.RenderTransform = faceMove;
        var faceParts = new Grid();
        faceParts.Children.Add(new Border { Background = lip, CornerRadius = new CornerRadius(5), Margin = new Thickness(0, 4, 0, 0) });
        faceParts.Children.Add(face);
        var choose = Bare(faceParts);
        choose.Name = lab ? "PrimeChoose" : "BasicChoose";
        choose.PointerEntered += (_, _) => { if (!choose.IsPressed) faceMove.Y = -1; };
        choose.PointerExited += (_, _) => { if (!choose.IsPressed) faceMove.Y = 0; };
        choose.PropertyChanged += (_, e) => { if (e.Property == Button.IsPressedProperty) faceMove.Y = choose.IsPressed ? 4 : choose.IsPointerOver ? -1 : 0; };
        choose.HorizontalAlignment = HorizontalAlignment.Stretch;
        choose.Click += (_, _) => { OpenPlan(tier, lab ? "compare-lab" : "compare-vault"); Close(); };
        stub.Children.Add(choose);
        var ways = PayWays(tier, lab ? "compare-lab" : "compare-vault", stacked: true, ink: muted);
        ways.Margin = new Thickness(18, 0, 0, 0);
        Grid.SetColumn(ways, 1);
        stub.Children.Add(ways);
        var body = new Grid { RowDefinitions = new RowDefinitions("*,20,Auto") };
        body.Children.Add(main);
        var foot = new Border { Background = stubInk, CornerRadius = new CornerRadius(0, 0, 8, 8), Child = stub };
        Grid.SetRow(foot, 2);
        body.Children.Add(foot);
        var perf = new global::Avalonia.Controls.Shapes.Rectangle { Name = "Perforation", Height = 2, Stroke = muted, StrokeThickness = 2, StrokeDashArray = new AvaloniaList<double> { 0.1, 2.6 }, StrokeLineCap = PenLineCap.Round, Opacity = 0.55, Margin = new Thickness(18, 0), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetRow(perf, 1);
        body.Children.Add(perf);
        foreach (var side in new[] { HorizontalAlignment.Left, HorizontalAlignment.Right })
        {
            var notch = new global::Avalonia.Controls.Shapes.Ellipse { Name = "TicketNotch", Width = 22, Height = 22, Fill = B("#141020"), HorizontalAlignment = side, VerticalAlignment = VerticalAlignment.Center, Margin = side == HorizontalAlignment.Left ? new Thickness(-11, 0, 0, 0) : new Thickness(0, 0, -11, 0), IsHitTestVisible = false };
            Grid.SetRow(notch, 1);
            body.Children.Add(notch);
        }
        var foil = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative), GradientStops = new GradientStops { new(Color.Parse("#F6E3A1"), 0), new(Color.Parse("#B48C2C"), 0.28), new(Color.Parse("#FFF2C2"), 0.47), new(Color.Parse("#9E7A24"), 0.7), new(Color.Parse("#E7C86E"), 1) } };
        var ticket = new Border { Name = lab ? "PrimeTicket" : "BasicTicket", Background = stock, BorderBrush = lab ? foil : accent, BorderThickness = new Thickness(lab ? 3 : 0), CornerRadius = new CornerRadius(8), Child = body, RenderTransform = new TranslateTransform() };
        ticket.PointerEntered += (_, _) => HoverTicket(ticket, true);
        ticket.PointerExited += (_, _) => HoverTicket(ticket, false);
        _ticketMoves[ticket] = (TranslateTransform)ticket.RenderTransform!;
        _compareTickets.Add(ticket);
        return ticket;
    }
    // WPF Compare.cs:525-594: full motion translates, reduced motion fades, off settles at once.
    private static async Task Tween(Control target, AvaloniaProperty property, double from, double to, int ms, CancellationToken token = default)
    {
        var animation = new Animation { Duration = TimeSpan.FromMilliseconds(ms) };
        animation.Children.Add(new KeyFrame { Cue = new Cue(0), Setters = { new Setter(property, from) } });
        animation.Children.Add(new KeyFrame { Cue = new Cue(1), Setters = { new Setter(property, to) } });
        try { await animation.RunAsync(target, token); }
        catch (OperationCanceledException) { }
    }

    private void DealTickets()
    {
        if (CompareMotion == MotionLevel.Off) return;
        for (var i = 0; i < _compareTickets.Count; i++)
        {
            var ticket = _compareTickets[i];
            var delay = i * 80;
            void Deal()
            {
                if (!_comparing || !ticket.IsAttachedToVisualTree()) return;
                var level = CompareMotion;
                if (level == MotionLevel.Off) return;
                _ = Tween(ticket, OpacityProperty, 0, 1, GentleVisuals.RiseMs(GentleVisuals.FadeMs(240)));
                if (level == MotionLevel.Full) _ = Tween(ticket, TranslateTransform.YProperty, 26, 0, 340);
            }
            if (delay == 0) Dispatcher.UIThread.Post(Deal, DispatcherPriority.Loaded);
            else DispatcherTimer.RunOnce(Deal, TimeSpan.FromMilliseconds(delay));
        }
    }

    private readonly Dictionary<Border, CancellationTokenSource> _hoverMotion = new();
    private void HoverTicket(Border ticket, bool over)
    {
        if (_hoverMotion.Remove(ticket, out var old)) { old.Cancel(); old.Dispose(); }
        if (!_ticketMoves.TryGetValue(ticket, out var transform)) return;
        var from = transform.Y;
        transform.Y = over && CompareMotion == MotionLevel.Full ? -3 : 0;
        if (CompareMotion != MotionLevel.Full) return;
        var next = new CancellationTokenSource();
        _hoverMotion[ticket] = next;
        _ = Tween(ticket, TranslateTransform.YProperty, from, transform.Y, 160, next.Token);
    }

    private async void LeaveCompare()
    {
        if (_leaving) return;
        _billingMotion?.Cancel();
        foreach (var c in _hoverMotion.Values) { c.Cancel(); c.Dispose(); }
        _hoverMotion.Clear();
        void Finish()
        {
            _leaving = false;
            _card.IsHitTestVisible = true;
            if (!IsVisible) return;
            BuildOffer();
            _ = FillAsyncBits();
        }
        if (CompareMotion == MotionLevel.Off) { Finish(); return; }
        _leaving = true;
        _card.IsHitTestVisible = false;
        var fadeMs = GentleVisuals.FadeMs(190);
        if (CompareMotion == MotionLevel.Full)
            foreach (var ticket in _compareTickets) _ = Tween(ticket, TranslateTransform.YProperty, 0, 16, 170);
        await Tween(_body, OpacityProperty, 1, 0, fadeMs);
        Finish();
    }

    private bool _closingAnimation;
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (_closingAnimation || _leaving) { e.Cancel = true; return; }
        if (!_comparing || CompareMotion == MotionLevel.Off || !IsVisible) { base.OnClosing(e); return; }
        e.Cancel = true;
        _closingAnimation = true;
        if (ReferenceEquals(Open, this)) Open = null;
        _card.IsHitTestVisible = false;
        var fadeMs = GentleVisuals.FadeMs(190);
        _ = Tween(_card, OpacityProperty, 1, 0, fadeMs);
        if (CompareMotion == MotionLevel.Full)
            foreach (var ticket in _compareTickets) _ = Tween(ticket, TranslateTransform.YProperty, 0, 16, 170);
        DispatcherTimer.RunOnce(() =>
        {
            _closingAnimation = false;
            _comparing = false;
            Close();
        }, TimeSpan.FromMilliseconds(fadeMs + 20));
    }

}
