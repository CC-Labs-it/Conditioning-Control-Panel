using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Leash;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Leash;

// WPF LeashDrawerSection.OfferRow and the decline side of LeashAskCard.
public sealed class LeashOfferCard : Border
{
    internal LeashOfferCard() : this(new LeashOffer(new LeashPerson("sample", "Juno", null), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(1)), () => null) { }

    internal LeashOfferCard(LeashOffer offer, Func<ILeashService?> service)
    {
        Tag = "leash-offer-row:" + offer.From.Id;
        Background = new SolidColorBrush(Color.Parse("#24102F"));
        BorderBrush = new SolidColorBrush(Color.Parse("#B49552"));
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(12);
        Padding = new Thickness(10);
        Margin = new Thickness(0, 4, 0, 4);
        var body = new StackPanel { Spacing = 8 };
        Child = body;
        body.Children.Add(new TextBlock { Text = offer.From.Name + " " + Loc.Get("leash_ask_title"), TextWrapping = TextWrapping.Wrap });
        var look = new Button { Content = Loc.Get("leash_offer_look"), Tag = "leash-offer-look" };
        body.Children.Add(look);
        var ask = new StackPanel { IsVisible = false, Spacing = 6, Tag = "leash-offer-ask" };
        body.Children.Add(ask);
        look.Click += (_, _) => { ask.IsVisible = true; look.IsVisible = false; };
        foreach (var key in new[] { "leash_ask_p_sees", "leash_ask_p_rewards", "leash_ask_p_tasks", "leash_ask_p_cut", "leash_ask_b_level", "leash_ask_b_quiet" })
            ask.Children.Add(new TextBlock { Text = Loc.Get(key), TextWrapping = TextWrapping.Wrap });
        var no = new Button { Content = Loc.Get("leash_ask_no"), Tag = "leash-ask-no" };
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Tag = "leash-ask-error" };
        ask.Children.Add(no);
        ask.Children.Add(error);
        no.Click += async (_, _) =>
        {
            no.IsEnabled = false;
            var result = LeashAnswerResult.Failed;
            try { if (service() is { } s) result = await s.AnswerAsync(offer.From.Id, false, LeashIntensity.Standard); } catch { }
            if (result == LeashAnswerResult.Done) { ask.IsVisible = false; IsVisible = false; return; }
            error.Text = Loc.GetF(result switch { LeashAnswerResult.Gone => "leash_ask_err_gone", LeashAnswerResult.Off => "leash_ask_err_off", _ => "leash_ask_err_failed" }, offer.From.Name);
            no.IsEnabled = true;
        };
    }
}
