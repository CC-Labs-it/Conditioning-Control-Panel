// PORTED from ConditioningControlPanel/Controls/NavRail/SectionTabStrip.Help.cs (d0ec7aa8d, owner
// 2026-10-07): help badges and the window pill confirmation card. Colour math through Core.
// The window host is now available; window pills use the WPF ask-first card below.

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
        internal Control? ConfirmationCard => _confirm?.Child;

        private void AskBeforeOpening(NavTab tab)
        {
            var target = PillFor(tab.Key)?.Parent as Control ?? PillFor(tab.Key);
            if (target == null) { TabRequested?.Invoke(tab); return; }
            CloseConfirm();
            _confirmTab = tab;
            var yes = ConfirmButton(SafeLoc("nav_open_window_yes", "Open it"), true);
            var no = ConfirmButton(SafeLoc("nav_open_window_no", "Not now"), false);
            yes.Name = "NavOpenWindowYes";
            no.Name = "NavOpenWindowNo";
            global::Avalonia.Automation.AutomationProperties.SetAutomationId(yes, yes.Name);
            global::Avalonia.Automation.AutomationProperties.SetAutomationId(no, no.Name);
            yes.Click += (_, _) => AnswerConfirm(true);
            no.Click += (_, _) => AnswerConfirm(false);
            var card = new Border
            {
                CornerRadius = new CornerRadius(14), Padding = new Thickness(16, 12),
                MinWidth = 240, MaxWidth = 340,
                Background = new SolidColorBrush(Color.FromArgb(0xF5, 0x1B, 0x10, 0x26)),
                BorderBrush = new SolidColorBrush(NavStripRules.WithAlpha(_hue, 0.85)),
                BorderThickness = new Thickness(1.5),
                Child = new StackPanel { Children =
                {
                    new TextBlock { Text = string.Format(SafeLoc("nav_open_window_title", "Open {0}?"), SafeLoc(tab.LabelKey, tab.Key)),
                        FontSize = 14, FontWeight = FontWeight.Bold, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap },
                    new TextBlock { Text = SafeLoc("nav_open_window_body", "It opens in its own window."),
                        FontSize = 12.5, Margin = new Thickness(0, 3, 0, 12), Foreground = Brushes.White,
                        Opacity = 0.85, TextWrapping = TextWrapping.Wrap },
                    new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8, Children = { yes, no } },
                } },
            };
            // WPF PopupAnimation.Fade uses the platform's 150 ms fade above Off.
            if (Level != ConditioningControlPanel.Models.MotionLevel.Off)
            {
                card.Opacity = 0;
                card.Transitions = new global::Avalonia.Animation.Transitions
                {
                    new global::Avalonia.Animation.DoubleTransition
                    { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(150) },
                };
            }
            var popup = new Popup
            {
                PlacementTarget = target, Placement = PlacementMode.Bottom, VerticalOffset = 8,
                IsLightDismissEnabled = true, Child = card,
            };
            _confirm = popup;
            popup.Closed += (_, _) =>
            {
                if (_confirm != popup) return;
                _confirm = null;
                _confirmTab = null;
            };
            card.KeyDown += (_, e) =>
            {
                if (e.Key != Key.Escape) return;
                CloseConfirm();
                e.Handled = true;
                PillFor(tab.Key)?.Focus();
            };
            popup.IsOpen = true;
            card.Opacity = 1;
            yes.Focus();
        }

        internal void CloseConfirm()
        {
            var popup = _confirm;
            _confirm = null;
            _confirmTab = null;
            if (popup != null) popup.IsOpen = false;
        }

        private void AnswerConfirm(bool open)
        {
            var tab = _confirmTab;
            CloseConfirm();
            if (open && tab != null) { FxOnChosen(tab); TabRequested?.Invoke(tab); }
        }

        /// <summary>WPF ConfirmButton: own template (a rounded Border that dims to 0.85 on hover), hand
        /// cursor, text in a TextBlock so a '_' in a translation is never an access key.</summary>
        private Button ConfirmButton(string text, bool filled)
        {
            var button = new Button
            {
                Background = filled ? new SolidColorBrush(_hue) : new SolidColorBrush(Color.FromArgb(0x1F, 255, 255, 255)),
                Cursor = new Cursor(StandardCursorType.Hand),
                Content = new TextBlock
                {
                    Text = text, FontSize = 13, FontWeight = FontWeight.SemiBold,
                    Foreground = filled ? new SolidColorBrush(NavStripRules.ActiveTextOn(_hue)) : Brushes.White,
                },
                Template = new global::Avalonia.Controls.Templates.FuncControlTemplate<Button>((b, _) => new Border
                {
                    Name = "Bd", CornerRadius = new CornerRadius(11), Padding = new Thickness(14, 5, 14, 6),
                    [!Border.BackgroundProperty] = b[!BackgroundProperty],
                    Child = new global::Avalonia.Controls.Presenters.ContentPresenter
                    {
                        Name = "PART_ContentPresenter", HorizontalAlignment = HorizontalAlignment.Center,
                        [!global::Avalonia.Controls.Presenters.ContentPresenter.ContentProperty] = b[!ContentProperty],
                    },
                }),
            };
            button.PointerEntered += (_, _) => button.Opacity = 0.85;
            button.PointerExited += (_, _) => button.Opacity = 1;
            return button;
        }

        // ---- test seams ---------------------------------------------------------------------

        /// <summary>Test seam: the "?" badge drawn over a pill (null when the pill has no help line).</summary>
        internal Control? HelpBadgeFor(string key) => _helpBadges.TryGetValue(key, out var b) ? b : null;
    }
}
