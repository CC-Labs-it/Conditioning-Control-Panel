using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Animation;
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
    private bool _comparing, _resizePending;
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
        main.Children.Add(new TextBlock { Text = Loc.Get("vaultgate_ticket_admit").ToUpper(System.Globalization.CultureInfo.CurrentUICulture), Foreground = muted, FontSize = 10.5, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap });
        main.Children.Add(new TextBlock { Text = name, Foreground = accent, FontSize = 32, FontWeight = FontWeight.Bold });
        main.Children.Add(Para(Loc.Get(lab ? "vaultgate_tier_lab_tag" : "vaultgate_tier_vault_tag"), text, 15));
        var price = (StackPanel)Price(tier, compact: true, yearly: _yearly);
        price.Tag = text;
        price.RenderTransform = new TranslateTransform();
        PaintPrice(price, tier, true, _yearly, false);
        var priceRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        priceRow.Children.Add(price);
        var saving = new Border { Name = lab ? "PrimeSaving" : "BasicSaving", BorderBrush = accent, BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(30), Padding = new Thickness(6), IsHitTestVisible = false, Child = new TextBlock { Foreground = accent, FontWeight = FontWeight.Bold, FontSize = 12, TextWrapping = TextWrapping.Wrap } };
        Grid.SetColumn(saving, 1);
        priceRow.Children.Add(saving);
        _comparePrices.Add((tier, price, saving));
        main.Children.Add(priceRow);
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
        var choose = SmallButton(Loc.GetF("vaultgate_choose", name));
        choose.HorizontalAlignment = HorizontalAlignment.Stretch;
        choose.Click += (_, _) => { OpenPlan(tier, lab ? "compare-lab" : "compare-vault"); Close(); };
        stub.Children.Add(choose);
        var ways = PayWays(tier, lab ? "compare-lab" : "compare-vault", stacked: true, ink: muted);
        ways.Margin = new Thickness(18, 0, 0, 0);
        Grid.SetColumn(ways, 1);
        stub.Children.Add(ways);
        var body = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
        body.Children.Add(main);
        var foot = new Border { Background = stubInk, CornerRadius = new CornerRadius(0, 0, 8, 8), Child = stub };
        Grid.SetRow(foot, 1);
        body.Children.Add(foot);
        var ticket = new Border { Name = lab ? "PrimeTicket" : "BasicTicket", Background = stock, BorderBrush = accent, BorderThickness = new Thickness(lab ? 3 : 0), CornerRadius = new CornerRadius(8), Child = body, RenderTransform = new TranslateTransform() };
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

    private void LeaveCompare()
    {
        if (_card.Child is Grid prior && ReferenceEquals(_body.Parent, prior))
        {
            prior.Children.Remove(_body);
            _card.Child = _body;
        }
        _billingMotion?.Cancel();
        foreach (var c in _hoverMotion.Values) { c.Cancel(); c.Dispose(); }
        _hoverMotion.Clear();
        if (CompareMotion == MotionLevel.Off)
        {
            BuildOffer();
            _ = FillAsyncBits();
            return;
        }
        var old = _body;
        var tickets = _compareTickets.ToArray();
        _card.Child = null;
        _body = new StackPanel();
        var overlay = new Grid();
        overlay.Children.Add(_body);
        old.IsHitTestVisible = false;
        overlay.Children.Add(old);
        _card.Child = overlay;
        BuildOffer();
        _ = FillAsyncBits();
        var fadeMs = GentleVisuals.FadeMs(190);
        _ = Tween(old, OpacityProperty, 1, 0, fadeMs);
        if (CompareMotion == MotionLevel.Full)
            foreach (var ticket in tickets) _ = Tween(ticket, TranslateTransform.YProperty, 0, 16, 170);
        DispatcherTimer.RunOnce(() =>
        {
            if (!ReferenceEquals(_card.Child, overlay)) return;
            overlay.Children.Remove(old);
            overlay.Children.Remove(_body);
            _card.Child = _body;
        }, TimeSpan.FromMilliseconds(fadeMs + 20));
    }

    private bool _closingAnimation;
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (_closingAnimation) { e.Cancel = true; return; }
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
