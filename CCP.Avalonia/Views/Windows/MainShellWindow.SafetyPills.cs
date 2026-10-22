// PORTED from ConditioningControlPanel/MainWindow/MainWindow.LabTab.cs (WPF 7.1.5): WireWebcamActivePill
// (:134-153) and WebcamActivePill_Click (:477-492). The header "Camera active" pill is the camera's
// privacy stop. The mic pill twin lives in MainShellWindow.SheListening.cs.
// No Focus Gaze service exists on this head, so the click stops the Blink Trainer and the tracker
// (every consumer of WebcamTracker releases with it). Not Lockdown-gated, as WPF.

using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        private void InitializeWebcamActivePill()
        {
            var tracker = WebcamTracker.Instance;
            Action onState = () => Dispatcher.UIThread.Post(UpdateWebcamPill);
            // Opened fires on every Hide/Show; Closed only once.
            Opened += (_, _) => { tracker.StateChanged -= onState; tracker.StateChanged += onState; UpdateWebcamPill(); };
            Closed += (_, _) => tracker.StateChanged -= onState;
            if (Named<Border>("WebcamActivePill") is { } pill)
                pill.KeyDown += (_, e) =>   // P17: the privacy stop is keyboard-reachable
                {
                    if (e.Key is not (Key.Enter or Key.Space)) return;
                    e.Handled = true;
                    _ = StopWebcamFromPillAsync();
                };
        }

        /// <summary>WPF WireWebcamActivePill: lit while the tracker runs (Tracking / FaceLost).</summary>
        internal void UpdateWebcamPill()
        {
            if (Named<Border>("WebcamActivePill") is { } pill) pill.IsVisible = WebcamTracker.Instance.IsRunning;
        }

        private void WebcamActivePill_Click(object? sender, PointerPressedEventArgs e)
        {
            if (sender is global::Avalonia.Visual v && !e.GetCurrentPoint(v).Properties.IsLeftButtonPressed) return;   // WPF: left button
            _ = StopWebcamFromPillAsync();
        }

        /// <summary>WPF :477: Blink Trainer, then the camera. Awaited, never inline: the teardown joins the
        /// capture thread (up to 5 s) on a wedged driver.</summary>
        internal async System.Threading.Tasks.Task StopWebcamFromPillAsync()
        {
            try { Overlays.BlinkTrainerSession.Stop(); } catch (Exception ex) { Log.Debug(ex, "Webcam pill: blink trainer stop failed"); }
            try { await WebcamTracker.Instance.StopAsync(); }
            catch (Exception ex) { Log.Debug(ex, "Webcam pill: stop failed"); }
            UpdateWebcamPill();
        }
    }
}
