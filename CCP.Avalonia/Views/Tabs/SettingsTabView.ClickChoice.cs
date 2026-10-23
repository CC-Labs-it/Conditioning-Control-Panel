using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Tabs;

public partial class SettingsTabView
{
    private readonly DispatcherTimer _clickChoiceClose = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private bool _refreshingClickChoice, _clickChoicePinned;

    private void InitClickChoice()
    {
        EscapeClaim.Mark(ClickChoiceBody);
        _clickChoiceClose.Tick += (_, _) => ClickChoiceCloseElapsed();
        AttachedToVisualTree += (_, _) => RefreshClickPreference();
        DetachedFromVisualTree += (_, _) => CloseClickChoice();
        PropertyChanged += (_, e) => { if (e.Property == IsVisibleProperty && !IsVisible) CloseClickChoice(); };
        ClickChoiceBody.AddHandler(KeyDownEvent, ClickChoiceKeyDown, RoutingStrategies.Tunnel);
        InvertDashboardClicks.IsCheckedChanged += ClickChoiceChanged;
    }

    internal void RefreshClickPreference()
    {
        _refreshingClickChoice = true;
        try
        {
            DashToggleHint.IsVisible = ConditioningControlPanel.Services.DashboardToggleHintRule.ShouldShow(CoreSettings.Current.DashboardToggleHintUses);
            bool invert = CoreSettings.Current.DashboardInvertClicks;
            DashToggleHint.Text = Loc.Get(invert ? "dash_click_swapped" : "dash_toggle_hint");
            InvertDashboardClicks.IsChecked = invert;
            ClickChoiceDescription.Text = Loc.Get(invert ? "dash_click_swapped" : "dash_click_default");
        }
        finally { _refreshingClickChoice = false; }
    }

    private void HoverClickChoice(object? sender, PointerEventArgs e)
    {
        _clickChoiceClose.Stop();
        if (ClickChoicePopup.IsOpen) return;
        RefreshClickPreference();
        ClickChoicePopup.IsLightDismissEnabled = false; // hover never takes capture from the anchor
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

    private void ClickChoiceClosed(object? sender, EventArgs e)
    {
        _clickChoiceClose.Stop();
        _clickChoicePinned = false;
        ClickChoicePopup.IsLightDismissEnabled = false;
    }

    private void KeepClickChoiceOpen(object? sender, PointerEventArgs e) => _clickChoiceClose.Stop();
    private void ScheduleClickChoiceClose(object? sender, PointerEventArgs e)
    {
        _clickChoiceClose.Stop();
        if (!_clickChoicePinned) _clickChoiceClose.Start();
    }

    internal void ClickChoiceCloseElapsed()
    {
        _clickChoiceClose.Stop();
        if (!_clickChoicePinned && !ClickChoiceAnchor.IsPointerOver && !ClickChoiceBody.IsPointerOver && !ClickChoiceBody.IsKeyboardFocusWithin)
            ClickChoicePopup.IsOpen = false;
    }

    internal void CloseClickChoice()
    {
        _clickChoiceClose.Stop();
        ClickChoicePopup.IsOpen = false;
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
}
