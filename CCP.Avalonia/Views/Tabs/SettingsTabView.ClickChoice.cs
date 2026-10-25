using System;
using Avalonia;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Tabs;

public partial class SettingsTabView
{
    private readonly DispatcherTimer _clickChoiceClose = new() { Interval = TimeSpan.FromMilliseconds(Services.UI.DashboardClickRules.HoverCloseMilliseconds) };
    private bool _refreshingClickChoice, _clickChoicePinned;

    private void InitializeClickChoice()
    {
        EscapeClaim.Mark(ClickChoiceBody);
        _clickChoiceClose.Tick += (_, _) => CloseClickChoiceAfterDelay();
        ClickChoiceAnchor.PointerEntered += (_, _) => OpenClickChoice(pinned: false);
        ClickChoiceAnchor.PointerExited += (_, _) => ScheduleClickChoiceClose();
        ClickChoiceAnchor.Click += (_, _) => OpenClickChoice(pinned: true);
        ClickChoiceBody.PointerEntered += (_, _) => _clickChoiceClose.Stop();
        ClickChoiceBody.PointerExited += (_, _) => ScheduleClickChoiceClose();
        ClickChoiceBody.AddHandler(KeyDownEvent, ClickChoiceKeyDown, RoutingStrategies.Tunnel);
        ClickChoicePopup.Closed += (_, _) => { _clickChoiceClose.Stop(); _clickChoicePinned = false; ClickChoicePopup.IsLightDismissEnabled = false; };
        InvertDashboardClicks.IsCheckedChanged += (_, _) =>
        {
            if (_refreshingClickChoice) return;
            _clickChoicePinned = true;
            _clickChoiceClose.Stop();
            ClickChoicePopup.IsLightDismissEnabled = true;
            CoreSettings.Current.DashboardInvertClicks = InvertDashboardClicks.IsChecked == true;
            CoreSettings.Save();
            RefreshClickPreference();
        };
        AttachedToVisualTree += (_, _) => RefreshClickPreference();
        DetachedFromVisualTree += (_, _) => CloseClickChoice();
        PropertyChanged += (_, e) => { if (e.Property == IsVisibleProperty && !IsVisible) CloseClickChoice(); };
        RefreshClickPreference();
    }

    internal void RefreshClickPreference()
    {
        _refreshingClickChoice = true;
        try
        {
            var invert = CoreSettings.Current.DashboardInvertClicks;
            InvertDashboardClicks.IsChecked = invert;
            ClickChoiceDescription.Text = Loc.Get(invert ? "dash_click_swapped" : "dash_click_default");
            DashToggleHint.Text = Loc.Get(invert ? "dash_click_swapped" : "dash_toggle_hint");
            DashToggleHint.IsVisible = Services.DashboardToggleHintRule.ShouldShow(CoreSettings.Current.DashboardToggleHintUses);
        }
        finally { _refreshingClickChoice = false; }
    }

    internal void OpenClickChoice(bool pinned)
    {
        _clickChoiceClose.Stop();
        if (!pinned && ClickChoicePopup.IsOpen) return;
        RefreshClickPreference();
        if (pinned) _clickChoicePinned = true;
        ClickChoicePopup.IsLightDismissEnabled = _clickChoicePinned;
        ClickChoicePopup.IsOpen = true;
        if (pinned) InvertDashboardClicks.Focus();
    }

    private void ScheduleClickChoiceClose()
    {
        _clickChoiceClose.Stop();
        if (!_clickChoicePinned) _clickChoiceClose.Start();
    }

    internal void CloseClickChoiceAfterDelay()
    {
        _clickChoiceClose.Stop();
        if (Services.UI.DashboardClickRules.ShouldClose(_clickChoicePinned, ClickChoiceAnchor.IsPointerOver,
            ClickChoiceBody.IsPointerOver, ClickChoiceBody.IsKeyboardFocusWithin)) ClickChoicePopup.IsOpen = false;
    }

    internal void ClickChoiceKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        EscapeClaim.Taken();
        CloseClickChoice();
        e.Handled = true;
        ClickChoiceAnchor.Focus();
    }

    private void CloseClickChoice()
    {
        _clickChoiceClose.Stop();
        ClickChoicePopup.IsOpen = false;
    }
}
