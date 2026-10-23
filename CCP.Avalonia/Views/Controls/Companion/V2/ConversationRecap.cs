using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Companion;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Companion.V2;

/// <summary>PORTED from WPF V2/ConversationRecap.cs: the exact rolling context that can accompany a later message.</summary>
internal sealed class ConversationRecap : StackPanel
{
    internal Button? ClearButton { get; }

    internal ConversationRecap()
    {
        var quotes = ConversationRules.Recap(App.Brain);
        var owner = App.Brain?.Memory;
        if (quotes.Count == 0) { IsVisible = false; return; }
        Margin = new Thickness(0, 0, 0, 18);
        Children.Add(new TextBlock { Text = Loc.Get("companion_v2_recap"), FontSize = 16, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        foreach (var quote in quotes)
            Children.Add(new TextBox { Text = quote, IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
                Background = Brushes.Transparent, Foreground = Brushes.White, BorderThickness = new Thickness(0), Margin = new Thickness(0, 0, 0, 8) });
        ClearButton = new Button { Content = new TextBlock { Text = Loc.Get("companion_v2_recap_clear") }, HorizontalAlignment = HorizontalAlignment.Left };
        ClearButton.Click += (_, _) => { ConversationRules.ClearRecap(App.Brain, owner); IsVisible = false; };
        Children.Add(ClearButton);
    }
}
