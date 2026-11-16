using System;
using System.Collections.Generic;
using System.Linq;
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
    // WPF Compare.cs:58-59, 230, 548, 568, 582, 642: things move unless motion is Off or Gentle Visuals is on.
    private static bool Moving => CompareMotion != MotionLevel.Off && !GentleVisuals.On;

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
        var title = Heading(Loc.Get("vaultgate_compare_title"));
        title.FontSize = 25;
        title.Margin = new Thickness(0, 0, 20, 0);
        var lever = BillingSwitch();
        Grid.SetColumn(lever, 1);
        _body.Children.Add(new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Children = { title, lever } });
        var basic = Ticket(1);
        var prime = Ticket(2);
        Grid.SetColumn(prime, 2);
        _body.Children.Add(new Grid { Margin = new Thickness(0, 14, 0, 0), ColumnDefinitions = new ColumnDefinitions("*,26,*"), Children = { basic, prime } });
        _yearNote = Para(Loc.Get("vaultgate_yearly_note"), Gold, 12, new Thickness(0, 12, 0, 0));
        _body.Children.Add(_yearNote);
        var back = Link(Loc.Get("vaultgate_back"));
        back.Margin = new Thickness(0, 0, 28, 0);
        back.Click += (_, _) => LeaveCompare();
        var fine = Fine(Loc.Get("vaultgate_fine"));
        fine.Margin = new Thickness(0);
        fine.TextAlignment = TextAlignment.Left;
        Grid.SetColumn(fine, 1);
        _body.Children.Add(new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(0, 8, 0, 0), Children = { back, fine } });
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
        if (animate && Moving) _ = Tween(_knob, TranslateTransform.XProperty, oldX, slide.X, 160, token);
        _yearNote.Opacity = _yearly ? 1 : 0;
        for (var i = 0; i < _comparePrices.Count; i++)
        {
            var (tier, price, saving) = _comparePrices[i];
            var off = PlanCatalog.YearlyOffPercent(_plans.For(tier));
            ((TextBlock)((Border)saving.Child!).Child!).Text = off > 0 ? Loc.GetF("vaultgate_stamp_off", off) : "";
            var show = _yearly && off > 0;
            saving.Opacity = show ? 0.9 : 0;
            // WPF Compare.cs:509-521: the round stamp thumps in after the roll or lifts off.
            if (animate && CompareMotion != MotionLevel.Off && show)
            {
                _ = Tween(saving, OpacityProperty, 0, 0.9, GentleVisuals.RiseMs(GentleVisuals.FadeMs(120)), token, 150);
                if (Moving)
                    foreach (var axis in new[] { ScaleTransform.ScaleXProperty, ScaleTransform.ScaleYProperty })
                        _ = Tween(saving, axis, 1.45, 1, 230, token, 150);
            }
            else if (animate && CompareMotion != MotionLevel.Off && off > 0) _ = Tween(saving, OpacityProperty, 0.9, 0, GentleVisuals.FadeMs(140), token);
            var slotIndex = _priceSlots.FindIndex(s => ReferenceEquals(s.Slot, price));
            if (slotIndex >= 0) _priceSlots[slotIndex] = (price, tier, true, _yearly, false);
            var oldAmount = (price.Children.FirstOrDefault() as WrapPanel)?.Children.OfType<Grid>().FirstOrDefault()?.Children.OfType<TextBlock>().LastOrDefault();
            var oldSub = (price.Children.ElementAtOrDefault(1) as TextBlock)?.Text;
            PaintPrice(price, tier, true, _yearly, false);
            if (animate && CompareMotion != MotionLevel.Off) _ = RollAmount(price, oldAmount, oldSub, token);
        }
    }

    // WPF Compare.cs:478-486, 528-552: the old amount leaves one way and the new one arrives from the
    // other inside the clipped window while the subline fades separately. Gentle: a plain crossfade.
    private async Task RollAmount(StackPanel price, TextBlock? old, string? oldSub, CancellationToken token)
    {
        var window = (Grid)((WrapPanel)price.Children[0]).Children[0];
        var next = (TextBlock)window.Children[0];
        var full = Moving;
        var dir = _yearly ? 1 : -1;
        if (old != null && old.Text != next.Text)
        {
            (old.Parent as Panel)?.Children.Remove(old);
            window.Children.Insert(0, old);
            _ = Tween(next, OpacityProperty, 0, 1, GentleVisuals.RiseMs(GentleVisuals.FadeMs(200)), token, full ? 40 : 0);
            if (full)
            {
                _ = Tween(old, TranslateTransform.YProperty, 0, -20 * dir, 130, token);
                _ = Tween(next, TranslateTransform.YProperty, 22 * dir, 0, 260, token, 40);
            }
            old.Opacity = 0;
            _ = Tween(old, OpacityProperty, 1, 0, GentleVisuals.FadeMs(130), token).ContinueWith(_ => window.Children.Remove(old), TaskScheduler.FromCurrentSynchronizationContext());
        }
        if (price.Children.ElementAtOrDefault(1) is not TextBlock sub || oldSub == null || sub.Text == oldSub) return;
        var subText = sub.Text;
        sub.Text = oldSub;
        sub.Opacity = 0;
        await Tween(sub, OpacityProperty, 1, 0, GentleVisuals.FadeMs(90), token);
        sub.Text = subText;
        sub.Opacity = 1;
        if (!token.IsCancellationRequested) await Tween(sub, OpacityProperty, 0, 1, GentleVisuals.RiseMs(GentleVisuals.FadeMs(180)), token);
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
        var main = new StackPanel { Margin = new Thickness(26, 16, 26, 4) };
        var top = new Grid { Name = "TicketHeading", ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var who = new StackPanel();
        who.Children.Add(new TextBlock { Text = Loc.Get("vaultgate_ticket_admit").ToUpper(System.Globalization.CultureInfo.CurrentUICulture), Foreground = muted, FontSize = 10.5, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap });
        who.Children.Add(new TextBlock { Text = name, Foreground = accent, FontSize = 32, FontWeight = FontWeight.Bold });
        who.Children.Add(Para(Loc.Get(lab ? "vaultgate_tier_lab_tag" : "vaultgate_tier_vault_tag"), text, 15));
        top.Children.Add(who);
        var price = (StackPanel)Price(tier, compact: true, yearly: _yearly);
        var salePanel = new StackPanel { Name = lab ? "PrimeSale" : "BasicSale" };
        price.Tag = (accent, salePanel);
        price.Margin = new Thickness(0);
        price.MaxWidth = 185;
        price.RenderTransform = new TranslateTransform();
        PaintPrice(price, tier, true, _yearly, false);
        // WPF Compare.cs:416-460: a tilted double-ruled price stamp, the round saving stamp parked over its corner.
        var priceRow = new Grid { Name = "TicketPrice", VerticalAlignment = VerticalAlignment.Top };
        priceRow.Children.Add(new Border
        {
            Name = "PriceStamp", BorderBrush = accent, BorderThickness = new Thickness(2.5), CornerRadius = new CornerRadius(3), Padding = new Thickness(2.5),
            Child = new Border { BorderBrush = accent, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(1.5), Padding = new Thickness(11, 3, 11, 7), ClipToBounds = true, Child = price },
            RenderTransform = new RotateTransform(-4), Opacity = 0.93, Margin = new Thickness(0, 14, 6, 0), VerticalAlignment = VerticalAlignment.Top,
        });
        var saving = new Border
        {
            Name = lab ? "PrimeSaving" : "BasicSaving", Width = 84, Height = 84, CornerRadius = new CornerRadius(42), BorderBrush = accent, BorderThickness = new Thickness(2.5), Padding = new Thickness(2.5),
            Child = new Border
            {
                CornerRadius = new CornerRadius(39), BorderBrush = accent, BorderThickness = new Thickness(1),
                Child = new TextBlock { Foreground = accent, FontWeight = FontWeight.Bold, FontSize = 13, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 2) },
            },
            RenderTransform = new TransformGroup { Children = { new ScaleTransform(1, 1), new RotateTransform(-11) } },
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(-70, 30, 0, 0), IsHitTestVisible = false, Opacity = 0,
        };
        priceRow.Children.Add(saving);
        _comparePrices.Add((tier, price, saving));
        Grid.SetColumn(priceRow, 1);
        top.Children.Add(priceRow);
        main.Children.Add(top);
        main.Children.Add(salePanel);
        main.Children.Add(new Border { Height = 1, Background = accent, Opacity = 0.4, Margin = new Thickness(0, 10, 0, 9) });
        void Perk(string number, string words, IBrush ink)
        {
            var line = new Grid { ColumnDefinitions = new ColumnDefinitions(lab ? "48,*" : "30,*"), Margin = new Thickness(0, 0, 0, lab ? 6 : 3) };
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
        var faceParts = new Grid { Children = { new Border { Background = lip, CornerRadius = new CornerRadius(5), Margin = new Thickness(0, 4, 0, 0) }, face } };
        var choose = Bare(faceParts);
        choose.Name = lab ? "PrimeChoose" : "BasicChoose";
        choose.PointerEntered += (_, _) => { if (!choose.IsPressed) faceMove.Y = -1; };
        choose.PointerExited += (_, _) => { if (!choose.IsPressed) faceMove.Y = 0; };
        choose.PropertyChanged += (_, e) => { if (e.Property == Button.IsPressedProperty) faceMove.Y = choose.IsPressed ? 4 : choose.IsPointerOver ? -1 : 0; };
        choose.HorizontalAlignment = HorizontalAlignment.Stretch;
        Border? self = null;
        choose.AddHandler(PointerPressedEvent, (_, e) => { if (self != null && e.GetCurrentPoint(choose).Properties.IsLeftButtonPressed) PressTicket(self, true); }, RoutingStrategies.Tunnel, handledEventsToo: true);
        choose.AddHandler(PointerReleasedEvent, (_, e) => { if (self != null && e.InitialPressMouseButton == MouseButton.Left) PressTicket(self, false); }, RoutingStrategies.Tunnel, handledEventsToo: true);
        choose.Click += (_, _) => { OpenPlan(tier, lab ? "compare-lab" : "compare-vault"); Close(); };
        stub.Children.Add(choose);
        var ways = PayWays(tier, lab ? "compare-lab" : "compare-vault", stacked: true, ink: muted);
        ways.Margin = new Thickness(18, 0, 0, 0);
        Grid.SetColumn(ways, 1);
        stub.Children.Add(ways);
        var foot = new Border { Background = stubInk, CornerRadius = new CornerRadius(0, 0, 8, 8), Child = stub };
        Grid.SetRow(foot, 2);
        var perf = new global::Avalonia.Controls.Shapes.Rectangle { Name = "Perforation", Height = 2, Stroke = muted, StrokeThickness = 2, StrokeDashArray = new AvaloniaList<double> { 0.1, 2.6 }, StrokeLineCap = PenLineCap.Round, Opacity = 0.55, Margin = new Thickness(18, 0), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetRow(perf, 1);
        var body = new Grid { RowDefinitions = new RowDefinitions("*,20,Auto"), Children = { main, foot, perf } };
        foreach (var side in new[] { HorizontalAlignment.Left, HorizontalAlignment.Right })
        {
            var notch = new global::Avalonia.Controls.Shapes.Ellipse { Name = "TicketNotch", Width = 22, Height = 22, Fill = B("#141020"), HorizontalAlignment = side, VerticalAlignment = VerticalAlignment.Center, Margin = side == HorizontalAlignment.Left ? new Thickness(-11, 0, 0, 0) : new Thickness(0, 0, -11, 0), IsHitTestVisible = false };
            Grid.SetRow(notch, 1);
            body.Children.Add(notch);
        }
        var foil = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative), GradientStops = new GradientStops { new(Color.Parse("#F6E3A1"), 0), new(Color.Parse("#B48C2C"), 0.28), new(Color.Parse("#FFF2C2"), 0.47), new(Color.Parse("#9E7A24"), 0.7), new(Color.Parse("#E7C86E"), 1) } };
        var ticket = new Border { Name = lab ? "PrimeTicket" : "BasicTicket", Background = stock, BorderBrush = lab ? foil : accent, BorderThickness = new Thickness(lab ? 3 : 0), CornerRadius = new CornerRadius(8), Child = body, RenderTransform = new TranslateTransform() };
        self = ticket;
        ticket.PointerEntered += (_, _) => HoverTicket(ticket, true);
        ticket.PointerExited += (_, _) => HoverTicket(ticket, false);
        _ticketMoves[ticket] = (TranslateTransform)ticket.RenderTransform!;
        _compareTickets.Add(ticket);
        return ticket;
    }
    // WPF Compare.cs:525-594: full motion translates, reduced motion fades, off settles at once.
    private static async Task Tween(Control target, AvaloniaProperty property, double from, double to, int ms, CancellationToken token = default, int delayMs = 0, bool hold = false)
    {
        // Backward fill holds the start value through the delay, as WPF BeginTime does. The value
        // after the run is the base value, so callers set the base to the end state first.
        var animation = new Animation { Duration = TimeSpan.FromMilliseconds(ms), Delay = TimeSpan.FromMilliseconds(delayMs), FillMode = delayMs > 0 || hold ? FillMode.Backward : FillMode.None,
            Children = { new KeyFrame { Cue = new Cue(0), Setters = { new Setter(property, from) } }, new KeyFrame { Cue = new Cue(1), Setters = { new Setter(property, to) } } } };
        try { await animation.RunAsync(target, token); }
        catch (OperationCanceledException) { }
    }

    // WPF Compare.cs:560-571: both tweens start now; Backward fill holds each ticket hidden (and when
    // moving lowered) through its 80 ms turn instead of drawing it in place first.
    private void DealTickets()
    {
        var level = CompareMotion;
        if (level == MotionLevel.Off) return;
        for (var i = 0; i < _compareTickets.Count; i++)
        {
            var ticket = _compareTickets[i];
            _ = Tween(ticket, OpacityProperty, 0, 1, GentleVisuals.RiseMs(GentleVisuals.FadeMs(240)), delayMs: i * 80, hold: true);
            if (Moving) _ = Tween(ticket, TranslateTransform.YProperty, 26, 0, 340, delayMs: i * 80, hold: true);
        }
    }

    private readonly Dictionary<Border, CancellationTokenSource> _hoverMotion = new();
    // WPF Compare.cs:368-380, 640-643: hover lifts -3, a press on Choose sets the ticket down +2, at
    // every motion level; it tweens only when moving.
    private readonly HashSet<Border> _pressedTickets = new(), _overTickets = new();
    private void HoverTicket(Border ticket, bool over)
    {
        if (over) _overTickets.Add(ticket);
        else { _overTickets.Remove(ticket); _pressedTickets.Remove(ticket); }
        SettleTicket(ticket);
    }

    private void PressTicket(Border ticket, bool pressed)
    {
        if (pressed) _pressedTickets.Add(ticket); else _pressedTickets.Remove(ticket);
        SettleTicket(ticket);
    }

    private void SettleTicket(Border ticket)
    {
        if (_hoverMotion.Remove(ticket, out var old)) { old.Cancel(); old.Dispose(); }
        if (!_ticketMoves.TryGetValue(ticket, out var transform)) return;
        var pressed = _pressedTickets.Contains(ticket);
        var from = transform.Y;
        transform.Y = pressed ? 2 : _overTickets.Contains(ticket) ? -3 : 0;
        if (!Moving || from == transform.Y) return;
        var next = new CancellationTokenSource();
        _hoverMotion[ticket] = next;
        _ = Tween(ticket, TranslateTransform.YProperty, from, transform.Y, pressed ? 90 : 160, next.Token);
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
            // WPF Compare.cs:112: the offer fades back in; Off stays immediate.
            _body.Opacity = 1;
            if (CompareMotion != MotionLevel.Off) _ = Tween(_body, OpacityProperty, 0, 1, GentleVisuals.RiseMs(GentleVisuals.FadeMs(200)), hold: true);
        }
        if (CompareMotion == MotionLevel.Off) { Finish(); return; }
        _leaving = true;
        _card.IsHitTestVisible = false;
        var fadeMs = GentleVisuals.FadeMs(190);
        if (Moving)
            foreach (var ticket in _compareTickets) _ = Tween(ticket, TranslateTransform.YProperty, 0, 16, 170);
        _body.Opacity = 0;
        await Tween(_body, OpacityProperty, 1, 0, fadeMs);
        Finish();
    }

    private bool _closingAnimation;
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // WPF Compare.cs:596-611 fades only the dialog's own close; an owner, app or OS close must not wait.
        if (e.CloseReason != WindowCloseReason.WindowClosing) { base.OnClosing(e); return; }
        if (_closingAnimation || _leaving) { e.Cancel = true; return; }
        if (!_comparing || CompareMotion == MotionLevel.Off || !IsVisible) { base.OnClosing(e); return; }
        e.Cancel = true;
        _closingAnimation = true;
        if (ReferenceEquals(Open, this)) Open = null;
        _card.IsHitTestVisible = false;
        var fadeMs = GentleVisuals.FadeMs(190);
        _card.Opacity = 0;
        _ = Tween(_card, OpacityProperty, 1, 0, fadeMs);
        if (Moving)
            foreach (var ticket in _compareTickets) _ = Tween(ticket, TranslateTransform.YProperty, 0, 16, 170);
        DispatcherTimer.RunOnce(() =>
        {
            _closingAnimation = false;
            _comparing = false;
            if (IsVisible) Close(); // the owner may have closed it mid-fade
        }, TimeSpan.FromMilliseconds(fadeMs + 20));
    }

}
