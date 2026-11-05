using System;
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

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    // Stage/retarget adapted from parity/trunk a4ee52d40cd347d288dcf7fdaa8445b79f8e5038.
    // The LIVE half of the coach mark.
    //
    // WPF's overlay is a second, topmost, full-screen layered window with a hole cut out of its OS
    // region. On this head a live tour never opens a window at all: the card and the dim are moved
    // into the OverlayLayer of the window the tour is about. Three rules fall out of that for free,
    // on Windows and Linux alike, with no platform call:
    //   - it never takes the pointer from another app (it is not on the desktop, only in our window);
    //   - it never steals focus from another app (nothing is shown or activated);
    //   - the spotlight hole is real (the dim is a Path with the hole excluded; an empty Canvas does
    //     not hit-test, so a click in the hole lands on the control under it).
    // Inside our own window the dim still absorbs clicks outside the hole and the card, as WPF's does
    // (TutorialStep.BlockBackgroundClicks).
    //
    // The parameterless render constructor still shows the real window (RenderProof needs a Window).
    public partial class TutorialOverlay
    {
        private Control? _stage;                 // RootGrid once it lives in a window's OverlayLayer
        private OverlayLayer? _layer;
        private IDisposable? _layerBoundsSub;
        private bool _tornDown;

        /// <summary>The tour's card left the screen (finished, skipped, panic, host closed). Hides
        /// Window.Closed: a live overlay is never a shown window, so the base event would never fire.</summary>
        public new event EventHandler? Closed;

        /// <summary>True while the live card is on a window.</summary>
        internal bool HasClosed => _tornDown;

        // WPF NewEnhancementDialog.xaml.cs:60-102 hides the coachmark around the picker.
        public new void Hide()
        {
            if (!_live) { base.Hide(); return; }
            Detach();
        }

        internal bool IsOnStage => _stage != null && _layer != null;

        /// <summary>The window the card is on right now.</summary>
        internal Window? StageWindow => _targetWindow;
        internal Control? StageRoot => _stage;

        private double StageW => _stage != null ? _stage.Bounds.Width : Bounds.Width;
        private double StageH => _stage != null ? _stage.Bounds.Height : Bounds.Height;

        /// <summary>Live: put the card on the target window. Sample: show the render window.</summary>
        public new void Show()
        {
            if (!_live || _targetWindow == null) { base.Show(); return; }
            if (_tornDown || IsOnStage) return;
            if (!AttachTo(_targetWindow))
            {
                Serilog.Log.Warning("Tutorial: {Window} has no overlay layer, the tour cannot draw; ending it", _targetWindow.GetType().Name);
                Close();
                return;
            }
            _loaded = true;
            if (CoreTutorial.CurrentStep is { } first) UpdateStep(first);
        }

        /// <summary>Take the card down. Ends a tour that is still running (the abandon route).</summary>
        public new void Close()
        {
            if (!_live) { base.Close(); return; }
            Teardown();
        }

        private void Teardown()
        {
            if (_tornDown) return;
            _tornDown = true;
            _closed = true;
            try { _spotlightDelayTimer?.Stop(); } catch { /* already stopped */ }
            _spotlightDelayTimer = null;
            UnsubscribeAdvanceTrigger();
            CoreTutorial.StepChanged -= OnSeamStepChanged;
            CoreTutorialEvents.Event -= OnTutorialEvent;
            CoreTutorial.Finished -= OnSeamFinished;
            Detach();
            // The host window closing must not strand a running tour (WPF OnClosed does the same).
            if (CoreTutorial.IsActive) CoreTutorial.Skip();
            base.Close(); // dispose the never-shown Window platform implementation too
            try { Closed?.Invoke(this, EventArgs.Empty); } catch { /* a subscriber's fault is not the tour's */ }
        }

        private bool AttachTo(Window window)
        {
            OverlayLayer? layer = null;
            try { layer = OverlayLayer.GetOverlayLayer(window); } catch { /* template not applied */ }
            if (layer == null) return false;

            var root = _stage ?? (Content as Control);
            if (root == null) return false;
            if (_stage == null) Content = null;     // out of the never-shown window, once

            _stage = root;
            _layer = layer;
            root.Width = layer.Bounds.Width;
            root.Height = layer.Bounds.Height;
            layer.Children.Add(root);
            _layerBoundsSub = layer.GetObservable(BoundsProperty).Subscribe(new BoundsObserver(this));

            window.AddHandler(KeyDownEvent, OnStageKeyDown, RoutingStrategies.Tunnel);
            window.Closed += OnStageWindowClosed;
            return true;
        }

        private void Detach()
        {
            try { _layerBoundsSub?.Dispose(); } catch { }
            _layerBoundsSub = null;
            if (_targetWindow != null)
            {
                try { _targetWindow.RemoveHandler(KeyDownEvent, OnStageKeyDown); } catch { }
                _targetWindow.Closed -= OnStageWindowClosed;
            }
            if (_layer != null && _stage != null)
            {
                try { _layer.Children.Remove(_stage); } catch { }
            }
            _layer = null;
        }

        private sealed class BoundsObserver : IObserver<Rect>
        {
            private readonly TutorialOverlay _o;
            public BoundsObserver(TutorialOverlay o) => _o = o;
            public void OnCompleted() { }
            public void OnError(Exception error) { }
            public void OnNext(Rect value) => _o.OnLayerBounds(value);
        }

        private void OnLayerBounds(Rect b)
        {
            if (_stage == null || _tornDown) return;
            _stage.Width = b.Width;
            _stage.Height = b.Height;
            // Re-measure after this layout pass: the target moved with the window's size.
            Dispatcher.UIThread.Post(() =>
            {
                if (_tornDown || _step == null) return;
                try { UpdateSpotlight(_step); } catch { /* a step must never take the window down */ }
            }, DispatcherPriority.Background);
        }

        private void OnStageWindowClosed(object? sender, EventArgs e)
        {
            // Scripted host swaps may close the old dialog before opening the next one.
            Dispatcher.UIThread.Post(() =>
            {
                if (_tornDown || !ReferenceEquals(sender, _targetWindow)) return;
                // WPF TutorialOverlay.xaml.cs:264-282: any named target window (current or next) means a
                // scripted cross-window jump is pending, so the tour waits for WindowLoaded/Next to retarget.
                if (!string.IsNullOrEmpty(CoreTutorial.CurrentStep?.TargetWindowTypeName)) return;
                var service = TutorialHead.Service;
                var nextIndex = service.CurrentStepIndex + 1;
                if (nextIndex < service.CurrentSteps.Count
                    && !string.IsNullOrEmpty(service.CurrentSteps[nextIndex].TargetWindowTypeName)) return;
                Teardown();
            }, DispatcherPriority.Background);
        }

        // WPF TutorialOverlay.OnKeyDown: Escape abandons the tour. Tunnelled on the HOST window, since
        // the card never takes keyboard focus. WPF only saw Escape while its overlay had focus, so an
        // Escape typed into one of the host's text boxes stays with that box.
        private void OnStageKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape || _tornDown || e.Source is TextBox) return;
            SkipTutorial();
            e.Handled = true;
        }

        // ---- retarget (WPF RetargetToWindow / FindWindowByTypeName) --------------------------

        internal static Func<System.Collections.Generic.IEnumerable<Window>> Windows { get; set; } = () =>
            (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Windows
                ?? Array.Empty<Window>();

        private static Window? FindWindowByTypeName(string typeName)
        {
            try
            {
                return Windows().LastOrDefault(w => w.GetType().Name == typeName && w.IsVisible);
            }
            catch { return null; }
        }

        /// <summary>Move the card onto another window (a step names the window its target is in).</summary>
        internal void RetargetToWindow(Window newWindow)
        {
            if (_tornDown || ReferenceEquals(newWindow, _targetWindow)) return;
            UnsubscribeAdvanceTrigger();
            Detach();
            _targetWindow = newWindow;
            if (!AttachTo(newWindow)) { Teardown(); return; }
            if (CoreTutorial.CurrentStep is { } step) UpdateStep(step);
        }

        /// <summary>True when the step's window is not the one the card is on and it could be moved
        /// there (UpdateStep ran again from RetargetToWindow, so the caller returns).</summary>
        private bool RetargetIfNeeded(CoreTutorial.Step step)
        {
            if (!_live || step.TargetWindowTypeName == null || _targetWindow == null) return false;
            if (_targetWindow.GetType().Name == step.TargetWindowTypeName) return false;
            var w = FindWindowByTypeName(step.TargetWindowTypeName);
            if (w == null || ReferenceEquals(w, _targetWindow)) return false;   // WindowLoaded:* brings us later
            RetargetToWindow(w);
            return true;
        }

        private void OnWindowLoadedEvent(string typeName)
        {
            if (CoreTutorial.CurrentStep is { } step && step.TargetWindowTypeName == typeName
                && FindWindowByTypeName(typeName) is { } w)
                RetargetToWindow(w);
            else
            {
                var service = TutorialHead.Service;
                var next = service.CurrentStepIndex + 1;
                if (next < service.CurrentSteps.Count && service.CurrentSteps[next].TargetWindowTypeName == typeName)
                    Advance(); // WPF fallback when WindowLoaded precedes the opening button's Click.
            }
        }

        // ---- the head step behind the card ---------------------------------------------------

        private HeadStep? HeadStepFor(CoreTutorial.Step step)
        {
            var head = TutorialHead.CurrentHeadStep;
            return head != null && head.Id == step.Id ? head : null;
        }

        /// <summary>WPF UpdateSpotlight: run the step's prep so its target can be measured.</summary>
        private void PrepareTarget(CoreTutorial.Step step)
        {
            if (_targetWindow == null) return;
            try { HeadStepFor(step)?.PrepareTargetWindowAction?.Invoke(_targetWindow); }
            catch { /* a tour never blocks on UI quirks */ }
        }

    }
}
