// PORTED from ConditioningControlPanel/Controls/NavRail/SectionTabStrip.Help.cs (d0ec7aa8d, owner
// 2026-10-07; declutter wave 1, WPF f04bd6a52e): the strip's one help button for the lit pill and
// the window pill confirmation card. Colour math through Core.
// The window host is now available; window pills use the WPF ask-first card below.

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.UI;

namespace ConditioningControlPanel.Avalonia.Views.Controls
{
    public sealed partial class SectionTabStrip
    {
        // Declutter wave 1 (2026-11-15, WPF f04bd6a52e): the per-pill "?" badges became ONE help
        // button at the strip's right end. It shows the help card of the pill that is lit, on
        // hover, on click and on keyboard focus + Enter. Every pill still says the same line in
        // its own tooltip, so "hover before clicking" keeps working without a badge per pill.
        internal const double HelpButtonSize = 22;

        private static readonly IBrush HelpHover = new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
        private Button _helpButton = null!;
        private bool _helpShown;
        private string? _helpKey;
        private string _helpLine = string.Empty;

        /// <summary>The loc key of a pill's "what is this" line (WPF NavStripRules.HelpKey).</summary>
        internal static string HelpKey(NavTab tab) => "nav_help_" + tab.Key;

        /// <summary>The "what is this" line: its own nav_help key, else the older label_tip line.</summary>
        internal static string HelpText(NavTab tab) =>
            SafeLoc(HelpKey(tab), SafeLoc(tab.LabelKey + "_tip", string.Empty));

        /// <summary>The strip's one help button (WPF HelpButton in SectionTabStrip.xaml): neutral
        /// chrome, no hue, no border at rest. Avalonia has no Visibility.Hidden, so "hidden" is
        /// clear, not hit-testable and not focusable: the row keeps its width.</summary>
        private Button BuildHelpButton()
        {
            var face = new Border
            {
                Width = HelpButtonSize, Height = HelpButtonSize, CornerRadius = new CornerRadius(HelpButtonSize / 2),
                Background = Brushes.Transparent, BorderThickness = new Thickness(2), BorderBrush = Brushes.Transparent,
                Child = new TextBlock
                {
                    Text = "?", FontSize = 13, FontWeight = FontWeight.Bold, Margin = new Thickness(0, -1, 0, 0),
                    Foreground = new SolidColorBrush(NavStripRules.TextLight),
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                },
            };
            var button = new Button
            {
                Name = "NavStripHelp", Content = face, Padding = new Thickness(0), Margin = new Thickness(2, 0, 0, 0),
                Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center, Cursor = new Cursor(StandardCursorType.Help),
                Template = PlainTemplate(), Opacity = 0, IsHitTestVisible = false, Focusable = false,
            };
            global::Avalonia.Automation.AutomationProperties.SetAutomationId(button, "NavStripHelp");
            ToolTip.SetShowDelay(button, 60);
            ToolTip.SetPlacement(button, PlacementMode.Bottom);
            button.PointerEntered += (_, _) => face.Background = HelpHover;
            // The card closes when the pointer or the focus leaves.
            button.PointerExited += (_, _) => { face.Background = Brushes.Transparent; ToolTip.SetIsOpen(button, false); };
            button.GotFocus += (_, e) => { if (e.NavigationMethod != NavigationMethod.Pointer) face.BorderBrush = FocusRing; };
            button.LostFocus += (_, _) => { face.BorderBrush = Brushes.Transparent; ToolTip.SetIsOpen(button, false); };
            // A click (or Enter / Space) is a request to read: open the card now.
            button.Click += (_, e) => { e.Handled = true; if (_helpShown) ToolTip.SetIsOpen(button, true); };
            return button;
        }

        /// <summary>Point the one help button at the lit pill: its card, its name for UIA. No lit
        /// pill, or a pill with no help line, hides the button (the row keeps its width).</summary>
        private void RefreshHelp()
        {
            ToolTip.SetIsOpen(_helpButton, false);
            var part = _activePill == null ? null : Part(_activePill);
            var help = part == null ? string.Empty : HelpText(part.Tab);
            _helpShown = part != null && _section != null && !string.IsNullOrEmpty(help);
            bool hadFocus = _helpButton.IsFocused;
            _helpButton.Opacity = _helpShown ? 1 : 0;
            _helpButton.IsHitTestVisible = _helpButton.Focusable = _helpShown;
            if (!_helpShown)
            {
                // "Hidden" here is clear + not focusable, which does not take focus away: a
                // section change that hides the button while it holds the keyboard hands focus to
                // the lit pill (else the first pill), never to an invisible control.
                if (hadFocus) MoveFocusOffHiddenHelp();
                ToolTip.SetTip(_helpButton, null);
                _helpKey = null;
                _helpLine = string.Empty;
                return;
            }
            var label = part!.Label.Text ?? string.Empty;
            ToolTip.SetTip(_helpButton, HelpCard(_section!, part.Tab, label, help));
            global::Avalonia.Automation.AutomationProperties.SetName(_helpButton, "? " + label);
            global::Avalonia.Automation.AutomationProperties.SetHelpText(_helpButton, help);
            _helpKey = part.Tab.Key;
            _helpLine = help;
        }

        private void MoveFocusOffHiddenHelp()
        {
            var target = (_activePill == null ? null : PillFor(_activePill))
                ?? (_pills.Count > 0 ? _pills[0].Pill : null);
            if (target is { IsEffectivelyVisible: true, Focusable: true } && target.Focus(NavigationMethod.Directional)) return;
            TopLevel.GetTopLevel(this)?.FocusManager?.Focus(null);   // no pill to take it: clear it
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
        private BeatLoop? _confirmFade;
        internal Popup? ConfirmationPopup => _confirm;
        internal string? ConfirmingKey => _confirmTab?.Key;
        internal Control? ConfirmationCard => _confirm?.Child;

        private void AskBeforeOpening(NavTab tab)
        {
            var target = (Control?)PillFor(tab.Key)?.Parent ?? PillFor(tab.Key);
            if (target == null) { TabRequested?.Invoke(tab); return; }
            CloseConfirm();
            _confirmTab = tab;
            var yes = ConfirmButton(SafeLoc("nav_open_window_yes", "Open it"), true, _hue);
            var no = ConfirmButton(SafeLoc("nav_open_window_no", "Not now"), false, _hue);
            yes.Name = "NavOpenWindowYes";
            no.Name = "NavOpenWindowNo";
            global::Avalonia.Automation.AutomationProperties.SetAutomationId(yes, "NavOpenWindowYes");
            global::Avalonia.Automation.AutomationProperties.SetAutomationId(no, "NavOpenWindowNo");
            yes.Click += (_, _) => AnswerConfirm(true);
            no.Click += (_, _) => AnswerConfirm(false);
            no.Margin = new Thickness(8, 0, 0, 0);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right, Children = { yes, no } };
            var body = new StackPanel { Children =
            {
                new TextBlock { Text = string.Format(SafeLoc("nav_open_window_title", "Open {0}?"),
                    SafeLoc(tab.LabelKey, tab.Key)), FontSize = 14, FontWeight = FontWeight.Bold, Foreground = Brushes.White,
                    TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = SafeLoc("nav_open_window_body", "It opens in its own window."),
                    FontSize = 12.5, Margin = new Thickness(0, 3, 0, 12),
                    Foreground = new SolidColorBrush(Color.FromArgb(0xD9, 255, 255, 255)), TextWrapping = TextWrapping.Wrap }, buttons,
            } };
            var card = new Border { CornerRadius = new CornerRadius(14), Padding = new Thickness(16, 12),
                MinWidth = 240, MaxWidth = 340, Background = new SolidColorBrush(Color.Parse("#F51B1026")),
                BorderBrush = new SolidColorBrush(NavStripRules.WithAlpha(_hue, 0.85)), BorderThickness = new Thickness(1.5), Child = body };
            var popup = new Popup
            {
                PlacementTarget = target, Placement = PlacementMode.Bottom, VerticalOffset = 8,
                IsLightDismissEnabled = true,
                Child = card,
            };
            ((ISetLogicalParent)popup).SetParent(this); // Avalonia needs a tree owner to show the card.
            _confirm = popup;
            // Keys inside a popup never reach the shell (a Popup has no interactive parent), so the card closes
            // itself on Escape and hands focus back to its pill, as PR #3461 did. The panic key is read by the
            // low-level hooks (Win32PanicKey, X11PanicKey), never by this routed event, so marking it handled
            // cannot shadow panic.
            card.KeyDown += (_, e) =>
            {
                if (e.Key != global::Avalonia.Input.Key.Escape) return;
                CloseConfirm();
                e.Handled = true;
                PillFor(tab.Key)?.Focus();
            };
            popup.Closed += (_, _) =>
            {
                if (ReferenceEquals(_confirm, popup)) { StopConfirmFade(); _confirm = null; _confirmTab = null; }
                ((ISetLogicalParent)popup).SetParent(null); // Release the strip's inheritance-child reference.
            };
            // WPF PopupAnimation.Fade: a 150 ms fade at Full and Reduced, none at Off.
            popup.Opened += (_, _) =>
            {
                if (Level == MotionLevel.Off) return;
                _confirmFade = new BeatLoop(card, seconds =>
                {
                    card.Opacity = Math.Clamp(seconds / 0.15, 0, 1);
                    if (seconds >= 0.15) StopConfirmFade();
                });
                _confirmFade.Start();
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

        internal void CloseConfirm()   // also the nav-window-confirm panic stop
        {
            var popup = _confirm;
            StopConfirmFade();
            _confirm = null;
            _confirmTab = null;
            if (popup != null) popup.IsOpen = false;
        }

        private void StopConfirmFade()
        {
            _confirmFade?.Stop();
            _confirmFade = null;
        }

        // WPF ConfirmButton: only this template paints the face, including its hover opacity.
        private static Button ConfirmButton(string text, bool filled, Color hue)
        {
            var button = new Button
            {
                MinWidth = 0, MinHeight = 0, Padding = new Thickness(14, 5, 14, 6),
                Background = new SolidColorBrush(filled ? hue : Color.FromArgb(0x1F, 255, 255, 255)),
                Cursor = new Cursor(StandardCursorType.Hand),
                Content = new TextBlock { Text = text, FontSize = 13, FontWeight = FontWeight.SemiBold,
                    Foreground = filled ? new SolidColorBrush(NavStripRules.ActiveTextOn(hue)) : Brushes.White },
            };
            button.Template = new FuncControlTemplate<Button>((b, _) =>
            {
                var face = new Border
                {
                    Name = "Bd", CornerRadius = new CornerRadius(11), Padding = b.Padding,
                    [!Border.BackgroundProperty] = b[!BackgroundProperty],
                    Child = new ContentPresenter { HorizontalAlignment = HorizontalAlignment.Center,
                        [!ContentPresenter.ContentProperty] = b[!ContentControl.ContentProperty] },
                };
                b.PropertyChanged += (_, e) =>
                {
                    if (e.Property == IsPointerOverProperty) face.Opacity = b.IsPointerOver ? 0.85 : 1;
                };
                return face;
            });
            return button;
        }

        internal void AnswerConfirmForTests(bool open) => AnswerConfirm(open);

        // ---- test seams ---------------------------------------------------------------------

        /// <summary>Test seam: the strip's one help button.</summary>
        internal Button HelpButtonForTests => _helpButton;

        /// <summary>Test seam: the pill whose help the button shows now (null = hidden), and its line.</summary>
        internal string? HelpShownFor => _helpShown ? _helpKey : null;
        internal string HelpLineShown => _helpShown ? _helpLine : string.Empty;
    }
}
