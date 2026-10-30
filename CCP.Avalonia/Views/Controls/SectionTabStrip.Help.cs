// PORTED from WPF SectionTabStrip.Help.cs (d0ec7aa8d): pill help and window confirmation.

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ConditioningControlPanel.Services.UI;

namespace ConditioningControlPanel.Avalonia.Views.Controls
{
    public sealed partial class SectionTabStrip
    {
        /// <summary>The badge's diameter, and how far it pokes past the pill's top and right edges.</summary>
        internal const double HelpBadgeSize = 17;
        internal const double HelpBadgeOverTop = 5;
        internal const double HelpBadgeOverRight = 4;

        private readonly Dictionary<string, Control> _helpBadges = new(StringComparer.Ordinal);

        /// <summary>The loc key of a pill's "what is this" line (WPF NavStripRules.HelpKey).</summary>
        internal static string HelpKey(NavTab tab) => "nav_help_" + tab.Key;

        /// <summary>The "what is this" line: its own nav_help key, else the older label_tip line.</summary>
        internal static string HelpText(NavTab tab) =>
            SafeLoc(HelpKey(tab), SafeLoc(tab.LabelKey + "_tip", string.Empty));

        /// <summary>The pill plus its "?" badge, in one host exactly the pill's size. The badge is a
        /// SIBLING of the pill, so hovering or clicking it never presses or navigates.</summary>
        private Control HostWithHelp(string section, NavTab tab, Button pill, string label, Color tint)
        {
            var host = new Grid { VerticalAlignment = VerticalAlignment.Center };
            host.Children.Add(pill);

            var help = HelpText(tab);
            if (string.IsNullOrEmpty(help)) return host;

            var rest = new SolidColorBrush(NavStripRules.WithAlpha(NavStripRules.DarkInk, 0.92));
            var lit = new SolidColorBrush(NavStripRules.Mix(tint, Colors.White, 0.15));
            var ring = new SolidColorBrush(NavStripRules.WithAlpha(NavStripRules.Mix(tint, Colors.White, 0.25), 0.95));
            var markInk = new SolidColorBrush(NavStripRules.DarkInk);
            var mark = new TextBlock
            {
                Text = "?", FontSize = 11, FontWeight = FontWeight.Bold, Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, -1, 0, 0),
            };
            var badge = new Border
            {
                Name = "NavPillHelp_" + tab.Key,
                Width = HelpBadgeSize, Height = HelpBadgeSize, CornerRadius = new CornerRadius(HelpBadgeSize / 2),
                Background = rest, BorderBrush = ring, BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, -HelpBadgeOverTop, -HelpBadgeOverRight, 0),
                Cursor = new Cursor(StandardCursorType.Help), Child = mark,
            };
            ToolTip.SetTip(badge, HelpCard(section, tab, label, help));
            ToolTip.SetShowDelay(badge, 60);
            ToolTip.SetPlacement(badge, PlacementMode.Bottom);
            global::Avalonia.Automation.AutomationProperties.SetName(badge, "? " + label);
            global::Avalonia.Automation.AutomationProperties.SetHelpText(badge, help);
            global::Avalonia.Automation.AutomationProperties.SetAutomationId(badge, "NavPillHelp_" + tab.Key);

            badge.PointerEntered += (_, _) => { badge.Background = lit; mark.Foreground = markInk; };
            badge.PointerExited += (_, _) => { badge.Background = rest; mark.Foreground = Brushes.White; };
            // A click on the badge is a request to read, not to go: open the card, swallow the click.
            badge.PointerPressed += (_, e) => { e.Handled = true; ToolTip.SetIsOpen(badge, true); };

            host.Children.Add(badge);
            _helpBadges[tab.Key] = badge;
            return host;
        }

        /// <summary>The hover card: the page name, what it does, and the tier line for a locked page.</summary>
        private Control HelpCard(string section, NavTab tab, string label, string help)
        {
            var body = new StackPanel { MaxWidth = 300 };
            body.Children.Add(new TextBlock
            {
                Text = $"{SafeLoc(NavSections.Find(section)?.LabelKey, section)} {SafeLoc("nav_crumb_sep", ">")} {label}",
                FontWeight = FontWeight.Bold, FontSize = 13, TextWrapping = TextWrapping.Wrap,
            });
            body.Children.Add(new TextBlock { Text = help, FontSize = 12.5, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap });
            string? tier = !PillLocked(tab.Tier) ? null
                : tab.Tier == 1 ? SafeLoc("nav_tag_premium_tip", string.Empty)
                : SafeLoc("nav_tag_lab_tip", string.Empty);
            if (!string.IsNullOrEmpty(tier))
                body.Children.Add(new TextBlock { Text = tier, FontSize = 11.5, Opacity = 0.75, Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap });
            return body;
        }

        private Popup? _confirm;
        private NavTab? _confirmTab;
        internal string? ConfirmingKey => _confirmTab?.Key;

        private void AskBeforeOpening(NavTab tab)
        {
            var target = PillFor(tab.Key);
            if (target == null) return;
            CloseConfirm();
            _confirmTab = tab;
            var yes = new Button { Content = SafeLoc("nav_open_window_yes", "Open it") };
            var no = new Button { Content = SafeLoc("nav_open_window_no", "Not now") };
            global::Avalonia.Automation.AutomationProperties.SetAutomationId(yes, "NavOpenWindowYes");
            global::Avalonia.Automation.AutomationProperties.SetAutomationId(no, "NavOpenWindowNo");
            yes.Click += (_, _) => AnswerConfirm(true);
            no.Click += (_, _) => AnswerConfirm(false);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8,
                HorizontalAlignment = HorizontalAlignment.Right, Children = { yes, no } };
            var body = new StackPanel { Spacing = 6, Children =
            {
                new TextBlock { Text = string.Format(SafeLoc("nav_open_window_title", "Open {0}?"),
                    SafeLoc(tab.LabelKey, tab.Key)), FontSize = 14, FontWeight = FontWeight.Bold,
                    TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = SafeLoc("nav_open_window_body", "It opens in its own window."),
                    TextWrapping = TextWrapping.Wrap }, buttons,
            } };
            var popup = new Popup
            {
                PlacementTarget = target, Placement = PlacementMode.Bottom, VerticalOffset = 8,
                IsLightDismissEnabled = true,
                Child = new Border { CornerRadius = new CornerRadius(14), Padding = new Thickness(16, 12),
                    MinWidth = 240, MaxWidth = 340, Background = new SolidColorBrush(Color.Parse("#F51B1026")),
                    BorderBrush = new SolidColorBrush(_hue), BorderThickness = new Thickness(1.5), Child = body },
            };
            _confirm = popup;
            popup.Closed += (_, _) =>
            {
                if (ReferenceEquals(_confirm, popup)) { _confirm = null; _confirmTab = null; }
            };
            popup.IsOpen = true;
            yes.Focus();
        }

        private void AnswerConfirm(bool open)
        {
            var tab = _confirmTab;
            CloseConfirm();
            if (open && tab != null) TabRequested?.Invoke(tab);
        }

        private void CloseConfirm()
        {
            var popup = _confirm;
            _confirm = null;
            _confirmTab = null;
            if (popup != null) popup.IsOpen = false;
        }

        internal void AnswerConfirmForTests(bool open) => AnswerConfirm(open);

        // ---- test seams ---------------------------------------------------------------------

        /// <summary>Test seam: the "?" badge drawn over a pill (null when the pill has no help line).</summary>
        internal Control? HelpBadgeFor(string key) => _helpBadges.TryGetValue(key, out var b) ? b : null;
    }
}
