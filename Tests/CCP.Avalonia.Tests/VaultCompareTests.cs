using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Invites;
using ConditioningControlPanel.Services.Vault;
using Xunit;

namespace CCP.Avalonia.Tests;

public class VaultCompareTests
{
    internal static void Click(Window window, string text)
    {
        var button = window.GetVisualDescendants().OfType<Button>()
            .First(b => b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == text));
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private static string?[] Texts(Window window) => window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToArray();

    [Fact]
    public Task TicketsSwitchPricesAndReturnToOffer() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var dialog = VaultGateDialog.ShowOffer(null, null, 1, () => { });
        try
        {
            Click(dialog, Loc.Get("vaultgate_compare"));
            Assert.Equal(980, dialog.Width);
            var tickets = dialog.GetVisualDescendants().OfType<Border>().Where(b => b.Name is "BasicTicket" or "PrimeTicket").ToArray();
            Assert.Equal(2, tickets.Length);
            Assert.NotEqual(((ISolidColorBrush)tickets[0].Background!).Color, ((ISolidColorBrush)tickets[1].Background!).Color);
            Assert.Equal(2, Texts(dialog).Count(t => t == Loc.Get("vaultgate_ticket_admit").ToUpper(System.Globalization.CultureInfo.CurrentUICulture)));
            Assert.Contains("01-10", Texts(dialog));
            var monthly = VaultOffer.Money(VaultOffer.PriceFor(1, VaultOffer.HeadlineCurrency(PlanCatalog.Current), PlanCatalog.Current).MonthlyCents, VaultOffer.HeadlineCurrency(PlanCatalog.Current));
            var yearly = VaultOffer.Money(VaultOffer.PriceFor(1, VaultOffer.HeadlineCurrency(PlanCatalog.Current), PlanCatalog.Current).YearlyCents, VaultOffer.HeadlineCurrency(PlanCatalog.Current));
            Assert.Contains(monthly, Texts(dialog));
            Click(dialog, Loc.GetF("vaultgate_billing_yearly", PlanCatalog.YearlyOffPercent(PlanCatalog.Current.Basic)));
            Assert.Contains(yearly, Texts(dialog));
            Assert.Equal(1, dialog.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == Loc.Get("vaultgate_yearly_note")).Opacity);
            Click(dialog, Loc.Get("vaultgate_billing_monthly"));
            Assert.Contains(monthly, Texts(dialog));
            Click(dialog, Loc.Get("vaultgate_back"));
            Assert.Equal(460, dialog.Width);
            Assert.Contains(Loc.Get("vaultgate_signin"), Texts(dialog));
        }
        finally { dialog.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task StampSitsBesideEachTicketHeading() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var dialog = VaultGateDialog.ShowOffer(null, null, 1, () => { });
        try
        {
            Click(dialog, Loc.Get("vaultgate_compare"));
            foreach (var (ticketName, tierName) in new[] { ("BasicTicket", "vaultgate_tier_vault_name"), ("PrimeTicket", "vaultgate_tier_lab_name") })
            {
                var ticket = dialog.GetVisualDescendants().OfType<Border>().Single(b => b.Name == ticketName);
                var stamp = ticket.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PriceStamp");
                var heading = ticket.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == Loc.Get(tierName));
                // WPF Compare.cs:269-291: heading column, then the stamp host in column 1 of the same grid.
                var host = Assert.IsType<Grid>(stamp.GetVisualParent());
                var grid = Assert.IsType<Grid>(host.GetVisualParent());
                Assert.Equal("TicketHeading", grid.Name);
                var headingColumn = Assert.IsType<StackPanel>(grid.Children[0]);
                Assert.Contains(heading, headingColumn.GetVisualDescendants());
                Assert.Equal(1, Grid.GetColumn(host));
                Assert.True(stamp.TranslatePoint(new Point(), grid)!.Value.X > heading.TranslatePoint(new Point(), grid)!.Value.X);
            }
        }
        finally { dialog.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task StampCaptionSitsBelowPriceForBothBillingChoices() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var oldSale = VaultGateDialog.Sale;
        var proxy = InviteApi.DefaultBaseUrl;
        InviteApi.DefaultBaseUrl = () => null;
        VaultGateDialog.Sale = new VaultSaleInfo(20, null, new[] { 1 });
        try
        {
            var dialog = VaultGateDialog.ShowOffer(null, null, 1, () => { });
            try
            {
                Click(dialog, Loc.Get("vaultgate_compare"));
                foreach (var yearly in new[] { false, true })
                {
                    if (yearly) Click(dialog, Loc.Get("vaultgate_billing_yearly"));
                    var ticket = dialog.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "BasicTicket");
                    var stamp = ticket.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PriceStamp");
                    var price = VaultOffer.PriceFor(1, VaultOffer.HeadlineCurrency(PlanCatalog.Current), PlanCatalog.Current);
                    var cents = yearly ? price.YearlyCents : VaultSale.FirstMonthCents(price.MonthlyCents, 20);
                    var amount = stamp.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == VaultOffer.Money(cents, VaultOffer.HeadlineCurrency(PlanCatalog.Current)));
                    var captionText = yearly ? Loc.GetF("vaultgate_price_year", VaultOffer.YearlyPerMonth(price)) : Loc.Get("vaultgate_sale_first");
                    var caption = stamp.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == captionText);
                    Assert.True(caption.TranslatePoint(new Point(), stamp)!.Value.Y >= amount.TranslatePoint(new Point(), stamp)!.Value.Y + amount.Bounds.Height - 2.5, $"{(yearly ? "yearly" : "monthly")}: caption {caption.TranslatePoint(new Point(), stamp)}, amount {amount.TranslatePoint(new Point(), stamp)} {amount.Bounds}");
                }
            }
            finally { dialog.Close(); }
        }
        finally { VaultGateDialog.Sale = oldSale; InviteApi.DefaultBaseUrl = proxy; }
        return Task.CompletedTask;
    });

    [Fact]
    public Task CompareSaleUsesDoubleRuleStampsAndStrikesMonthlyPrice() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var oldSale = VaultGateDialog.Sale;
        var proxy = InviteApi.DefaultBaseUrl;
        InviteApi.DefaultBaseUrl = () => null;
        VaultGateDialog.Sale = new VaultSaleInfo(20, null, new[] { 1 });
        try
        {
            var dialog = VaultGateDialog.ShowOffer(null, null, 1, () => { });
            try
            {
                Click(dialog, Loc.Get("vaultgate_compare"));
                var stamps = dialog.GetVisualDescendants().OfType<Border>().Where(b => b.Name == "PriceStamp").ToArray();
                Assert.Equal(2, stamps.Length);
                Assert.All(stamps, stamp => Assert.True(stamp.Child is Border inner && inner.BorderThickness.Left == 1));
                var struck = stamps[0].GetVisualDescendants().OfType<TextBlock>().Single(t => Equals(t.Tag, "struck"));
                Assert.Equal(TextDecorations.Strikethrough, struck.TextDecorations);
                Click(dialog, Loc.Get("vaultgate_billing_yearly"));
                Assert.DoesNotContain(dialog.GetVisualDescendants().OfType<TextBlock>(), t => Equals(t.Tag, "struck"));
            }
            finally { dialog.Close(); }
        }
        finally { VaultGateDialog.Sale = oldSale; InviteApi.DefaultBaseUrl = proxy; }
        return Task.CompletedTask;
    });

    [Fact]
    public Task SaleDetailsStayOutsideTheRotatedStamp() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var oldSale = VaultGateDialog.Sale;
        var proxy = InviteApi.DefaultBaseUrl;
        var sale = new VaultSaleInfo(20, DateTime.UtcNow.AddDays(10), new[] { 1 });
        InviteApi.DefaultBaseUrl = () => null;
        VaultGateDialog.Sale = sale;
        try
        {
            var dialog = VaultGateDialog.ShowOffer(null, null, 1, () => { });
            try
            {
                Click(dialog, Loc.Get("vaultgate_compare"));
                var ticket = dialog.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "BasicTicket");
                var stamp = ticket.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PriceStamp");
                var currency = VaultOffer.HeadlineCurrency(PlanCatalog.Current);
                var normal = VaultOffer.Money(VaultOffer.PriceFor(1, currency, PlanCatalog.Current).MonthlyCents, currency);
                var saleLine = Loc.GetF("vaultgate_sale_line", sale.Percent, normal);
                var endsLine = Loc.GetF("vaultgate_sale_ends", VaultSale.EndsText(sale)!);
                var stampTexts = stamp.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToArray();
                Assert.Contains(normal, stampTexts);
                Assert.Contains(Loc.Get("vaultgate_sale_first"), stampTexts);
                Assert.DoesNotContain(saleLine, stampTexts);
                Assert.DoesNotContain(endsLine, stampTexts);
                var ticketTexts = ticket.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToArray();
                Assert.Contains(saleLine, ticketTexts);
                Assert.Contains(endsLine, ticketTexts);
                VaultGateDialog.Sale = null;
                await dialog.FillPlansAsync();
                Assert.DoesNotContain(saleLine, Texts(dialog));
                Assert.DoesNotContain(endsLine, Texts(dialog));
                VaultGateDialog.Sale = sale;
                await dialog.FillPlansAsync();
                Assert.Contains(saleLine, Texts(dialog));
                Assert.Contains(endsLine, Texts(dialog));
                Assert.DoesNotContain(stamp.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == saleLine || t.Text == endsLine);
                Click(dialog, Loc.Get("vaultgate_billing_yearly"));
                Assert.DoesNotContain(saleLine, Texts(dialog));
                Assert.DoesNotContain(endsLine, Texts(dialog));
                Click(dialog, Loc.Get("vaultgate_billing_monthly"));
                Assert.Contains(saleLine, Texts(dialog));
                Assert.Contains(endsLine, Texts(dialog));
            }
            finally { dialog.Close(); }
        }
        finally { VaultGateDialog.Sale = oldSale; InviteApi.DefaultBaseUrl = proxy; }
    });

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public Task ChooseOpensTierPatreon(int tier) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var old = VaultGateDialog.Launch;
        var proxy = InviteApi.DefaultBaseUrl;
        string? opened = null;
        VaultGateDialog.Launch = url => { opened = url; return true; };
        InviteApi.DefaultBaseUrl = () => null;
        try
        {
            var dialog = VaultGateDialog.ShowOffer(null, null, 1, () => { });
            Click(dialog, Loc.Get("vaultgate_compare"));
            Click(dialog, Loc.GetF("vaultgate_choose", Loc.Get(tier == 1 ? "vaultgate_tier_vault_name" : "vaultgate_tier_lab_name")));
            Assert.Equal(PlanCatalog.Current.For(tier).PatreonUrl, opened);
            Assert.Null(VaultGateDialog.Open);
        }
        finally { VaultGateDialog.Open?.Close(); VaultGateDialog.Launch = old; InviteApi.DefaultBaseUrl = proxy; }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData("en")][InlineData("de")][InlineData("es")][InlineData("fr")][InlineData("it")]
    [InlineData("ja")][InlineData("ko")][InlineData("pt-BR")][InlineData("ru")][InlineData("zh-CN")]
    public Task CompareFitsSmallWindow(string language) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var loc = LocalizationManager.Instance;
        var previous = loc.CurrentLanguage;
        try
        {
            loc.SetLanguage(language);
            var dialog = VaultGateDialog.ShowOffer(null, null, 1, () => { });
            try
            {
                Click(dialog, Loc.Get("vaultgate_compare"));
                foreach (var yearly in new[] { false, true })
                {
                    if (yearly) Click(dialog, Loc.GetF("vaultgate_billing_yearly", PlanCatalog.YearlyOffPercent(PlanCatalog.Current.Basic)));
                    dialog.UpdateLayout();
                    var card = (Control)dialog.Content!;
                    card.Measure(new Size(980, double.PositiveInfinity));
                    Assert.True(card.DesiredSize.Width <= 1280, $"{language}: width {card.DesiredSize.Width}");
                    Assert.True(card.DesiredSize.Height <= 672, $"{language}: height {card.DesiredSize.Height}");
                }
            }
            finally { dialog.Close(); }
        }
        finally { loc.SetLanguage(previous); }
        return Task.CompletedTask;
    });
}
