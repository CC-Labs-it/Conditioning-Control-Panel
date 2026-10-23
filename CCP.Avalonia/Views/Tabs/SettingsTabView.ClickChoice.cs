// Adapted from WPF SettingsTabView.xaml.cs: click preference, 450 ms hover grace, pinned edit.
using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Views.Tabs;

public partial class SettingsTabView
{
    private readonly DispatcherTimer _clickChoiceClose = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private bool _refreshingClickChoice, _clickChoicePinned;
    private static SettingsTabView? _clickChoiceOwner;

    private void InitClickChoice()
    {
        EscapeClaim.Mark(ClickChoiceBody);
        ClickChoiceBody.AddHandler(InputElement.KeyDownEvent, ClickChoiceKeyDown, RoutingStrategies.Tunnel);
        _clickChoiceClose.Tick += (_, _) => ClickChoiceCloseElapsed();
        Loaded += (_, _) => { RefreshClickPreference(); RefreshV2Badges(); };
        Unloaded += (_, _) => CloseClickChoice();
        // DashToggleHint's text is chosen in code (swapped or not), so it is re-read on a language
        // change rather than carrying a {loc:Str} binding that would snap back to dash_toggle_hint.
        AttachedToVisualTree += (_, _) => LocalizationManager.Instance.LanguageChanged += OnClickChoiceLanguageChanged;
        DetachedFromVisualTree += (_, _) => LocalizationManager.Instance.LanguageChanged -= OnClickChoiceLanguageChanged;
        PropertyChanged += (_, e) =>
        {
            if (e.Property != IsVisibleProperty) return;
            if (!IsVisible) CloseClickChoice();
            else { RefreshClickPreference(); RefreshV2Badges(); }
        };
    }

    private void OnClickChoiceLanguageChanged(object? sender, EventArgs e) => RefreshClickPreference();

    internal void RefreshClickPreference()
    {
        _refreshingClickChoice = true;
        try
        {
            bool invert = CoreSettings.Current.DashboardInvertClicks;
            InvertDashboardClicks.IsChecked = invert;
            ClickChoiceDescription.Text = Loc.Get(invert ? "dash_click_swapped" : "dash_click_default");
            DashToggleHint.Text = Loc.Get(invert ? "dash_click_swapped" : "dash_toggle_hint");
            DashToggleHint.IsVisible = DashboardToggleHintRule.ShouldShow(CoreSettings.Current.DashboardToggleHintUses);
        }
        finally { _refreshingClickChoice = false; }
    }

    private void HoverClickChoice(object? sender, PointerEventArgs e)
    {
        _clickChoiceClose.Stop();
        if (ClickChoicePopup.IsOpen) return;
        RefreshClickPreference();
        ClickChoicePopup.IsLightDismissEnabled = false;
        ClickChoicePopup.IsOpen = true;
    }

    private void OpenClickChoice(object? sender, RoutedEventArgs e)
    {
        _clickChoiceClose.Stop();
        RefreshClickPreference();
        _clickChoicePinned = true;
        ClickChoicePopup.IsLightDismissEnabled = true;
        ClickChoicePopup.IsOpen = true;
        InvertDashboardClicks.Focus();
    }

    private void ClickChoiceOpened(object? sender, EventArgs e)
    {
        if (_clickChoiceOwner != this) _clickChoiceOwner?.CloseClickChoice();
        _clickChoiceOwner = this;
    }

    private void ClickChoiceClosed(object? sender, EventArgs e)
    {
        _clickChoiceClose.Stop();
        _clickChoicePinned = false;
        ClickChoicePopup.IsLightDismissEnabled = false;
        if (_clickChoiceOwner == this) _clickChoiceOwner = null;
    }

    private void KeepClickChoiceOpen(object? sender, PointerEventArgs e) => _clickChoiceClose.Stop();

    private void ScheduleClickChoiceClose(object? sender, PointerEventArgs e)
    {
        _clickChoiceClose.Stop();
        if (!_clickChoicePinned) _clickChoiceClose.Start();
    }

    private void ClickChoiceCloseElapsed()
    {
        _clickChoiceClose.Stop();
        if (!_clickChoicePinned && !ClickChoiceAnchor.IsPointerOver && !ClickChoiceBody.IsPointerOver
            && !ClickChoiceBody.IsKeyboardFocusWithin) CloseClickChoice();
    }

    private void ClickChoiceKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        EscapeClaim.Taken();
        CloseClickChoice();
        e.Handled = true;
        ClickChoiceAnchor.Focus();
    }

    private void ClickChoiceChanged(object? sender, RoutedEventArgs e)
    {
        if (_refreshingClickChoice) return;
        _clickChoicePinned = true;
        _clickChoiceClose.Stop();
        ClickChoicePopup.IsLightDismissEnabled = true;
        CoreSettings.Current.DashboardInvertClicks = InvertDashboardClicks.IsChecked == true;
        CoreSettings.Save();
        RefreshClickPreference();
    }

    private void CloseClickChoice()
    {
        _clickChoiceClose.Stop();
        ClickChoicePopup.IsOpen = false;
    }

    internal static void CloseClickChoiceForPanic() => _clickChoiceOwner?.CloseClickChoice();

    /// <summary>WPF SettingsTabView.RefreshV2Badges + MainWindow.RefreshMosaicTierBadges.</summary>
    internal void RefreshV2Badges()
    {
        // ponytail: svc-prize-ownership has no snapshot/GrantsChanged yet. Re-read the existing
        // ownership seam on load, Home return and wall repaint; Release remains unowned.
        CardFlash.ShowV2Badge = PrizeOwnership.IsGranted(PrizeOwnership.FlashDriftBounce)
            || PrizeOwnership.IsGranted(PrizeOwnership.FlashPendulum) || PrizeOwnership.IsGranted(PrizeOwnership.JackpotRemix);
        CardBubblePop.ShowV2Badge = PrizeOwnership.IsGranted(PrizeOwnership.BubbleRain)
            || PrizeOwnership.IsGranted(PrizeOwnership.BubbleSpiralIn);
    }
}
