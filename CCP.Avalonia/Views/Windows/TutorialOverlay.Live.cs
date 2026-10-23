using System;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Tours;
using HeadStep = ConditioningControlPanel.Avalonia.Tours.TutorialStep;
using HeadTrigger = ConditioningControlPanel.Avalonia.Tours.TutorialAdvanceTrigger;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

// Adapted from parity/trunk e5b3976dd, TutorialOverlay.Live.cs; native spotlight window retained.
// WPF TutorialOverlay.xaml.cs owns the trigger semantics, including deferred, once-only advance.
public partial class TutorialOverlay
{
    private bool _finished;
    private HeadStep? _subscribedStep;
    private Button? _subButton;
    private SelectingItemsControl? _subSelector;
    private Slider? _subSlider;
    private IDisposable? _subText;

    // A window lookup seam keeps headless tests off the desktop.
    internal static Func<string, Window?> FindTargetWindow = name =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Windows
            .LastOrDefault(w => w.GetType().Name == name && w.IsVisible);
    internal Window? TargetWindow => _targetWindow;

    public new void Show()
    {
        if (_live && _targetWindow != null) base.Show(TutorialHeadHooks.Shell ?? _targetWindow);
        else base.Show();
    }

    private void AttachToTarget(Window window)
    {
        window.PositionChanged += OnTargetMoved;
        window.SizeChanged += OnTargetResized;
        window.Closed += OnTargetClosed;
        // ShowActivated=false keeps typing in the target; Escape still abandons the tour.
        window.AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    private void DetachFromTarget(Window window)
    {
        window.PositionChanged -= OnTargetMoved;
        window.SizeChanged -= OnTargetResized;
        window.Closed -= OnTargetClosed;
        window.RemoveHandler(KeyDownEvent, OnKeyDown);
    }

    private void OnTargetMoved(object? sender, PixelPointEventArgs e) => UpdateOverlayPosition();
    private void OnTargetResized(object? sender, SizeChangedEventArgs e) => UpdateOverlayPosition();

    private void OnTargetClosed(object? sender, EventArgs e) => Dispatcher.UIThread.Post(() =>
    {
        if (_finished || !CoreTutorial.IsActive) return;
        var steps = TutorialHead.Service.CurrentSteps;
        var next = CoreTutorial.CurrentStepIndex + 1;
        // A scripted window swap is structural, even when its button's Click was lost (WPF :244).
        if (!string.IsNullOrEmpty(CoreTutorial.CurrentStep?.TargetWindowTypeName) ||
            (next < steps.Count && !string.IsNullOrEmpty(steps[next].TargetWindowTypeName))) return;
        CoreTutorial.Skip();
    }, DispatcherPriority.Background);

    public void RetargetToWindow(Window window)
    {
        if (_finished || ReferenceEquals(window, _targetWindow)) return;
        UnsubscribeAdvanceTrigger();
        if (_targetWindow != null) DetachFromTarget(_targetWindow);
        _targetWindow = window;
        AttachToTarget(window);
        UpdateOverlayPosition();
        if (CoreTutorial.CurrentStep is { } step) UpdateStep(step);
    }

    private void OnBusEvent(object? sender, string name)
    {
        // Capture the emitting card: a queued event must never advance its successor.
        var step = _step;
        Dispatcher.UIThread.Post(() =>
        {
            if (_finished || !CoreTutorial.IsActive || !ReferenceEquals(step, _step)) return;
            if (name.StartsWith("WindowLoaded:", StringComparison.Ordinal))
            {
                var type = name.Substring("WindowLoaded:".Length);
                if (step?.TargetWindowTypeName == type)
                {
                    if (FindTargetWindow(type) is { } window) RetargetToWindow(window);
                    return;
                }
                var steps = TutorialHead.Service.CurrentSteps;
                var next = CoreTutorial.CurrentStepIndex + 1;
                if (next < steps.Count && steps[next].TargetWindowTypeName == type) Advance();
                return;
            }
            if (_subscribedStep is { AdvanceTrigger: HeadTrigger.OnEvent } head && head.AdvanceEventName == name)
                Advance();
        });
    }

    private static HeadStep? HeadStepFor(CoreTutorial.Step step) =>
        TutorialHead.CurrentHeadStep is { } head && head.Id == step.Id ? head : null;

    private void PrepareTarget(CoreTutorial.Step step)
    {
        try { if (_targetWindow != null) HeadStepFor(step)?.PrepareTargetWindowAction?.Invoke(_targetWindow); }
        catch { /* a prep callback must not break a tour */ }
    }

    private void SubscribeAdvanceTrigger(CoreTutorial.Step step)
    {
        UnsubscribeAdvanceTrigger();
        _subscribedStep = HeadStepFor(step);
        if (_subscribedStep == null || _targetWindow == null) return;
        var target = string.IsNullOrEmpty(step.TargetElementName) ? null : FindElementByName(_targetWindow, step.TargetElementName);
        switch (_subscribedStep.AdvanceTrigger)
        {
            case HeadTrigger.OnButtonClick when target is Button button:
                _subButton = button;
                button.AddHandler(PointerReleasedEvent, OnSubButtonReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
                button.AddHandler(Button.ClickEvent, OnSubButtonClick, RoutingStrategies.Bubble, handledEventsToo: true);
                // ponytail: Avalonia has no readable DialogResult on Closing. The preview release
                // catches a confirm before its Click closes the parent; closing by X does not advance.
                break;
            case HeadTrigger.OnTextEquals when target is TextBox text:
                _subText = text.GetObservable(TextBox.TextProperty).Subscribe(new TextObserver(this));
                break;
            case HeadTrigger.OnSelectionEquals when target is SelectingItemsControl selector:
                _subSelector = selector;
                selector.SelectionChanged += OnSubSelectionChanged;
                break;
            case HeadTrigger.OnSliderAtLeast when target is Slider slider:
                _subSlider = slider;
                slider.AddHandler(PointerReleasedEvent, OnSubSliderReleased, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
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
        _subText?.Dispose();
        _subText = null;
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

    private sealed class TextObserver(TutorialOverlay overlay) : IObserver<string?>
    {
        private bool _initial = true;
        public void OnCompleted() { }
        public void OnError(Exception error) { }
        public void OnNext(string? value)
        {
            // WPF requires an edit, even if the value already matches when the step opens.
            if (_initial) { _initial = false; return; }
            if (overlay._subscribedStep is { } step && TextMatches(value ?? "", step.AdvanceValue ?? "")) overlay.Advance();
        }
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
        if (e.InitialPressMouseButton != MouseButton.Left) return;
        if (_subscribedStep is not { } step || _subSlider == null) return;
        var value = _subSlider.Value;
        if (value >= step.AdvanceMinValue && (double.IsNaN(step.AdvanceMaxValue) || value <= step.AdvanceMaxValue)) Advance();
    }

    internal static bool TextMatches(string actual, string expected)
    {
        actual = (actual ?? "").Trim();
        expected = (expected ?? "").Trim();
        if (expected.Length == 0) return false;
        if (string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)) return true;
        if (double.TryParse(actual, NumberStyles.Any, CultureInfo.InvariantCulture, out var av) &&
            double.TryParse(expected, NumberStyles.Any, CultureInfo.InvariantCulture, out var ev)) return Math.Abs(av - ev) < 0.5;
        return actual.IndexOf(expected, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
