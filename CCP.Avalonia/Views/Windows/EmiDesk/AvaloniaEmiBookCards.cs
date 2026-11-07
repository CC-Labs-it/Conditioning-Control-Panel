using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Services.EmiDesk;

namespace ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;

// B3: the Prize Counter is WPF desktop only in 7.2. Keep the shared WPF deck intact.
// Every Avalonia book index, bookmark, rail, dot and pager uses this same filtered order.
internal static class AvaloniaEmiBookCards
{
    private static readonly EmiBookCard[] Cards = EmiBookCards.All
        .Where(card => card.Id != "prize-counter").ToArray();

    internal static IReadOnlyList<EmiBookCard> All => Cards;

    internal static int IndexOf(string? id) => Array.FindIndex(Cards,
        card => string.Equals(card.Id, id, StringComparison.Ordinal));

    internal static int FirstOnTab(int tab) => Array.FindIndex(Cards, card => card.Tab == tab);

    internal static bool TabHasCards(int tab) => FirstOnTab(tab) >= 0;
}
