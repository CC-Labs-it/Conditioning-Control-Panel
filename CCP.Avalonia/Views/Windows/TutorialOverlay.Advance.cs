using System;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Tours;
using HeadStep = ConditioningControlPanel.Avalonia.Tours.TutorialStep;
using HeadTrigger = ConditioningControlPanel.Avalonia.Tours.TutorialAdvanceTrigger;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

// Adapted from parity/trunk e5b3976dd6ab2bf215b89c6e0719e7489dd153ed.
public partial class TutorialOverlay
{
    private bool _closed;
    private HeadStep? _subscribedStep;
    private Button? _subButton;
    private TextBox? _subTextBox;
    private SelectingItemsControl? _subSelector;
    private Slider? _subSlider;

    private void SubscribeAdvanceTrigger(CoreTutorial.Step coreStep)
    {
        UnsubscribeAdvanceTrigger();
        var step = TutorialHead.CurrentHeadStep;
        if (step == null || step.Id != coreStep.Id || _targetWindow == null) return;
        _subscribedStep = step;
        if (step.AdvanceTrigger is HeadTrigger.Manual or HeadTrigger.OnEvent) return;
        if (string.IsNullOrEmpty(step.TargetElementName)) return;
        var target = FindElementByName(_targetWindow, step.TargetElementName);
        switch (step.AdvanceTrigger)
        {
            case HeadTrigger.OnButtonClick when target is Button button:
                _subButton = button;
                // Tunnel before a dialog's own click handler can close its host. Advancing is
                // deferred, so the original click can finish before StepChanged rewrites the card.
                button.AddHandler(PointerReleasedEvent, OnSubButtonReleased, RoutingStrategies.Tunnel, true);
                button.AddHandler(Button.ClickEvent, OnSubButtonClick, RoutingStrategies.Bubble, true);
                break;
            case HeadTrigger.OnTextEquals when target is TextBox text:
                _subTextBox = text;
                // PropertyChanged has no initial emission: WPF requires an actual user edit,
                // even when the prefilled value already satisfies this card.
                text.PropertyChanged += OnSubTextChanged;
                break;
            case HeadTrigger.OnSelectionEquals when target is SelectingItemsControl selector:
                _subSelector = selector;
                selector.SelectionChanged += OnSubSelectionChanged;
                break;
            case HeadTrigger.OnSliderAtLeast when target is Slider slider:
                _subSlider = slider;
                slider.AddHandler(PointerReleasedEvent, OnSubSliderReleased, RoutingStrategies.Tunnel, true);
                break;
        }
    }

    private void UnsubscribeAdvanceTrigger()
    {
        if (_subButton != null)
        {
            _subButton.RemoveHandler(PointerReleasedEvent, OnSubButtonReleased);
            _subButton.RemoveHandler(Button.ClickEvent, OnSubButtonClick);
            _subButton = null;
        }
        if (_subTextBox != null) _subTextBox.PropertyChanged -= OnSubTextChanged;
        _subTextBox = null;
        if (_subSelector != null) _subSelector.SelectionChanged -= OnSubSelectionChanged;
        _subSelector = null;
        if (_subSlider != null) _subSlider.RemoveHandler(PointerReleasedEvent, OnSubSliderReleased);
        _subSlider = null;
        _subscribedStep = null;
    }

    private void OnSubButtonClick(object? sender, RoutedEventArgs e) => Advance();
    private void OnSubButtonReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Left) Advance();
    }

    private void OnSubTextChanged(object? sender, global::Avalonia.AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == TextBox.TextProperty && _subscribedStep is { } step &&
            TutorialAdvanceRules.TextMatches(_subTextBox?.Text, step.AdvanceValue)) Advance();
    }

    private void OnSubSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_subscribedStep is not { } step || _subSelector == null) return;
        var selected = _subSelector.SelectedItem;
        var actual = selected is ContentControl item
            ? (step.MatchByTag ? item.Tag : item.Content)?.ToString() : selected?.ToString();
        if (string.IsNullOrEmpty(step.AdvanceValue) ||
            (!string.IsNullOrEmpty(actual) && string.Equals(actual, step.AdvanceValue, StringComparison.OrdinalIgnoreCase))) Advance();
    }

    private void OnSubSliderReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Left || _subscribedStep is not { } step || _subSlider == null) return;
        var value = _subSlider.Value;
        if (value < step.AdvanceMinValue || (!double.IsNaN(step.AdvanceMaxValue) && value > step.AdvanceMaxValue)) return;
        Advance();
    }

    private void OnTutorialEvent(object? sender, string name)
    {
        // Capture the subscribed card before dispatching. A queued event from a completed card
        // must never advance a successor card or a newly started tour.
        var step = _step;
        Dispatcher.UIThread.Post(() =>
        {
            if (!_closed && ReferenceEquals(_step, step) && _subscribedStep is { } current &&
                current.AdvanceTrigger == HeadTrigger.OnEvent && current.AdvanceEventName == name) Advance();
        });
    }
}
