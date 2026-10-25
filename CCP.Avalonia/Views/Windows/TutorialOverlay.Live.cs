using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
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

    // A window lookup seam keeps headless tests off the desktop.
    internal static Func<string, Window?> FindTargetWindow = name =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Windows
            .LastOrDefault(w => w.GetType().Name == name && w.IsVisible);
    internal Window? TargetWindow => _targetWindow;

    public new void Show()
    {
        // Owned by the window it spotlights (Mod Creator, the editor, a dialog), not the shell: a
        // click through the hole activates that window, and an owned overlay stays above it.
        if (_live && _targetWindow is { IsVisible: true }) base.Show(_targetWindow);
        else base.Show();
    }

    private void AttachToTarget(Window window)
    {
        window.PositionChanged += OnTargetMoved;
        window.SizeChanged += OnTargetResized;
        window.Closed += OnTargetClosed;
        // ShowActivated=false keeps typing in the target; Escape still abandons the tour.
        window.AddHandler(KeyDownEvent, OnHostKeyDown, RoutingStrategies.Tunnel);
    }

    private void DetachFromTarget(Window window)
    {
        window.PositionChanged -= OnTargetMoved;
        window.SizeChanged -= OnTargetResized;
        window.Closed -= OnTargetClosed;
        window.RemoveHandler(KeyDownEvent, OnHostKeyDown);
    }

    // Escape typed into a host text box stays with the box (WPF only hears Escape on its own overlay).
    private void OnHostKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Source is not TextBox) OnKeyDown(sender, e);
    }

    private void OnTargetMoved(object? sender, PixelPointEventArgs e) => UpdateOverlayPosition();
    private void OnTargetResized(object? sender, SizeChangedEventArgs e) => UpdateOverlayPosition();

    private void OnTargetClosed(object? sender, EventArgs e) => Dispatcher.UIThread.Post(() =>
    {
        if (_finished || !CoreTutorial.IsActive || WindowSwapExpected()) return;
        CoreTutorial.Skip();
    }, DispatcherPriority.Background);

    // A scripted window swap is structural, even when its button's Click was lost (WPF :244).
    private static bool WindowSwapExpected()
    {
        var steps = TutorialHead.Service.CurrentSteps;
        var next = CoreTutorial.CurrentStepIndex + 1;
        return !string.IsNullOrEmpty(CoreTutorial.CurrentStep?.TargetWindowTypeName) ||
               (next < steps.Count && !string.IsNullOrEmpty(steps[next].TargetWindowTypeName));
    }

    // The host owns this overlay, so closing it would close the card too. During a scripted swap
    // (dialog -> editor) the card lets go first and waits, unowned, for the next host's WindowLoaded.
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (e.CloseReason == WindowCloseReason.OwnerWindowClosing && !_finished && CoreTutorial.IsActive && WindowSwapExpected())
            Owner = null;
        base.OnClosing(e);
    }

    public void RetargetToWindow(Window window)
    {
        if (_finished || ReferenceEquals(window, _targetWindow)) return;
        UnsubscribeAdvanceTrigger();
        if (_targetWindow != null) DetachFromTarget(_targetWindow);
        _targetWindow = window;
        AttachToTarget(window);
        // Re-own: the new host may be a separate window (Mod Creator, the editor) above the shell.
        if (IsVisible && window.IsVisible) Owner = window;
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
}
