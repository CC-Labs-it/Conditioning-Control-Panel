using System;
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
    private void FaceChrome(bool compare)
    {
        MinWidth = compare ? 980 : 0;
        Width = compare ? 980 : 460;
        _card.Background = compare ? B("#141020") : CardBg;
        _card.Padding = compare ? new Thickness(30, 20, 30, 14) : new Thickness(22, 20, 22, 18);
        _card.BorderThickness = new Thickness(compare ? 0 : 1.5);
    }

    private void BuildCompare()
    {
        FaceChrome(true);
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
        var note = Para(Loc.Get("vaultgate_yearly_note"), Gold, 12, new Thickness(0, 12, 0, 0));
        note.Opacity = _yearly ? 1 : 0;
        _body.Children.Add(note);
        var foot = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(0, 8, 0, 0) };
        var back = Link(Loc.Get("vaultgate_back"));
        back.Margin = new Thickness(0, 0, 28, 0);
        back.Click += (_, _) => { BuildOffer(); _ = FillAsyncBits(); };
        foot.Children.Add(back);
        var fine = Fine(Loc.Get("vaultgate_fine"));
        fine.Margin = new Thickness(0);
        fine.TextAlignment = TextAlignment.Left;
        Grid.SetColumn(fine, 1);
        foot.Children.Add(fine);
        _body.Children.Add(foot);
    }

    private Control BillingSwitch()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var yearly in new[] { false, true })
        {
            var label = yearly ? Loc.GetF("vaultgate_billing_yearly", PlanCatalog.YearlyOffPercent(_plans.Basic)) : Loc.Get("vaultgate_billing_monthly");
            var word = Bare(new TextBlock { Text = label, Foreground = yearly == _yearly ? Brushes.White : Dim, FontSize = 13 });
            word.Click += (_, _) => SetBilling(yearly);
            row.Children.Add(word);
            if (!yearly)
            {
                var knob = new Border { Width = 25, Height = 20, CornerRadius = new CornerRadius(4), Background = _yearly ? Gold : B("#FFF8EC"), HorizontalAlignment = _yearly ? HorizontalAlignment.Right : HorizontalAlignment.Left, Margin = new Thickness(3) };
                var track = Bare(new Border { Width = 60, Height = 28, CornerRadius = new CornerRadius(6), Background = B("#0C0815"), Child = knob });
                track.Margin = new Thickness(10, 0);
                track.Click += (_, _) => SetBilling(!_yearly);
                row.Children.Add(track);
            }
        }
        return row;
    }

    private void SetBilling(bool yearly)
    {
        if (_yearly == yearly) return;
        _yearly = yearly;
        BuildCompare();
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
        var saleDetails = new StackPanel();
        var price = (StackPanel)Price(tier, compact: true, yearly: _yearly, saleDetails: saleDetails);
        price.Tag = text;
        price.Margin = new Thickness(0);
        PaintPrice(price, tier, true, _yearly, false, saleDetails);
        // WPF Compare.cs:416-447: the price sits inside a tilted double-rule stamp.
        main.Children.Add(new Border
        {
            Name = "PriceStamp", BorderBrush = accent, BorderThickness = new Thickness(2.5),
            CornerRadius = new CornerRadius(3), Padding = new Thickness(2.5),
            HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 6, 0),
            RenderTransform = new RotateTransform(-4), RenderTransformOrigin = RelativePoint.Center,
            Opacity = 0.93,
            Child = new Border { BorderBrush = accent, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(1.5), Padding = new Thickness(11, 0, 11, 1), Child = price },
        });
        main.Children.Add(saleDetails);
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
        return new Border { Name = lab ? "PrimeTicket" : "BasicTicket", Background = stock, BorderBrush = accent, BorderThickness = new Thickness(lab ? 3 : 0), CornerRadius = new CornerRadius(8), Child = body };
    }
}
