using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Companion;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Companion
{
    /// <summary>WPF Views/Controls/Companion/V2/PerkPicker.cs: one radio row per perk, locked rows sit back.</summary>
    internal static class PerkPicker
    {
        // #1379: a locked perk row sits back so "can't be selected" does not read as broken.
        internal const double LockedRowOpacity = 0.45;
        internal static double RowOpacity(bool unlocked) => unlocked ? 1.0 : LockedRowOpacity;

        internal static Window Build()
        {
            var s = CoreSettings.Current;
            var active = CompanionPerks.Active(s);
            var panel = new StackPanel { Margin = new Thickness(22) };
            var window = new Window
            {
                Title = Loc.Get("companion_perk_picker"), Width = 510, SizeToContent = SizeToContent.Height, MaxHeight = 710,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, CanResize = false,
                Background = new SolidColorBrush(Color.FromRgb(27, 18, 37)), Foreground = Brushes.White,
                Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto },
            };
            panel.Children.Add(new TextBlock { Text = window.Title, FontSize = 24, Margin = new Thickness(0, 0, 0, 8) });
            panel.Children.Add(new TextBlock { Text = Loc.Get("companion_perk_hint"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14) });
            foreach (var type in Enum.GetValues<CompanionBonusType>())
            {
                var perk = CompanionPerks.For(type);
                var unlocked = CompanionPerks.CanSelect(type, s.PlayerLevel);
                var text = new StackPanel();
                text.Children.Add(new TextBlock { Text = Loc.Get(CompanionPerks.NameKey(type)), FontWeight = FontWeight.SemiBold, FontSize = 16 });
                text.Children.Add(new TextBlock { Text = Loc.Get(perk.LocKey), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 0) });
                if (!unlocked) text.Children.Add(new TextBlock { Text = Loc.GetF("companion_perk_unlock", CompanionPerks.RequiredLevel(type)), Margin = new Thickness(0, 5, 0, 0) });
                var choice = new RadioButton
                {
                    Content = text, IsChecked = active == type, IsEnabled = unlocked, Tag = type,
                    Opacity = RowOpacity(unlocked || active == type),
                    Foreground = perk.Negative ? Brushes.Salmon : Brushes.White, Padding = new Thickness(10),
                    Margin = new Thickness(0, 0, 0, 10), HorizontalContentAlignment = HorizontalAlignment.Stretch,
                };
                choice.IsCheckedChanged += (_, _) =>
                {
                    if (choice.IsChecked != true || !CompanionPerks.TrySelect(CoreSettings.Current, type)) return;
                    // ponytail: WPF SetPerk also restarts CompanionService's XP-drain timer; this head
                    // has no companion XP service yet (MainShellWindow.Companion.cs), so only the choice persists.
                    CoreSettings.Save();
                    window.Close();
                };
                panel.Children.Add(choice);
            }
            window.KeyDown += (_, e) => { if (e.Key == Key.Escape) window.Close(); };
            return window;
        }

        internal static Task Show(Window owner) => Build().ShowDialogSafe(owner);
    }
}
