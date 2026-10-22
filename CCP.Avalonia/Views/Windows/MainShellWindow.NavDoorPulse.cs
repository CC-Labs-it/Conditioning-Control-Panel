// The Programs first-visit announcement on the rail (WPF MainWindow.ProgramsTab.cs:48-67 and
// MainWindow.TabNavigation.cs:823-875): four soft opacity dips on the section row that owns the tab,
// started 1.2 s after the window comes up (MainWindow.xaml.cs:3814-3832) while HasSeenProgramsTab is
// unset, and spent by ShowTab("programs") on any route in.
using System;
using System.Collections.Generic;
using System.Threading;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.UI;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        private readonly Dictionary<string, (Button Header, CancellationTokenSource Cts)> _navHeaderPulses = new(StringComparer.Ordinal);
        private IDisposable? _programsPulseDelay;

        /// <summary>Test seam: which tabs' section rows are pulsing right now.</summary>
        internal bool IsNavDoorHeaderPulsing(string tabKey) => _navHeaderPulses.ContainsKey(tabKey);

        /// <summary>WPF MainWindow.xaml.cs:3814: once per launch, 1.2 s in, while the tab is unseen.</summary>
        private void InitializeProgramsTabPulse()
        {
            Closed += (_, _) =>
            {
                _programsPulseDelay?.Dispose();
                StopNavDoorHeaderPulse("programs");
            };
            if (CoreSettings.Current.HasSeenProgramsTab) return;
            _programsPulseDelay = DispatcherTimer.RunOnce(StartProgramsTabPulse, TimeSpan.FromMilliseconds(1200));
        }

        /// <summary>WPF StartProgramsTabPulse (:48): an ambient loop, so Motion's ambient gate first.</summary>
        internal void StartProgramsTabPulse()
        {
            if (CoreSettings.Current.HasSeenProgramsTab) return;   // reached in the 1.2 s
            if (!AmbientFxCanvas.Env.AllowAmbientLoops) return;
            StartNavDoorHeaderPulse("programs");
        }

        internal void StopProgramsTabPulse() => StopNavDoorHeaderPulse("programs");

        /// <summary>WPF StartNavDoorHeaderPulse (TabNavigation.cs:831): false when the owning section is
        /// already the lit one; true when the row took it, or the quiet window / Motion swallowed it.</summary>
        private bool StartNavDoorHeaderPulse(string tabKey)
        {
            try
            {
                if (Platform.StartupLadder.IsQuiet) return true;
                var section = NavSections.SectionForTab(tabKey);
                if (section == null) return false;
                var header = _navSectionRows.Find(r => r.Section == section)?.Button;
                if (header == null) return false;
                if (NavSections.SectionForTab(CurrentTab) == section) return false;
                if (_navHeaderPulses.ContainsKey(tabKey)) return true;
                if (!AmbientFxCanvas.Env.AllowTransitions) return true;

                var cts = new CancellationTokenSource();
                _navHeaderPulses[tabKey] = (header, cts);
                // WPF: 1.0 -> 0.35, AutoReverse, RepeatBehavior(4), SineEase InOut = eight halves.
                var anim = new Animation
                {
                    Duration = TimeSpan.FromMilliseconds(AmbientFxCanvas.Env.Level == MotionLevel.Reduced ? 350 : 700),
                    IterationCount = new IterationCount(8),
                    PlaybackDirection = PlaybackDirection.Alternate,
                    Easing = new SineEaseInOut(),
                    Children =
                    {
                        new KeyFrame { Cue = new Cue(0), Setters = { new Setter(OpacityProperty, 1.0) } },
                        new KeyFrame { Cue = new Cue(1), Setters = { new Setter(OpacityProperty, 0.35) } },
                    },
                };
                anim.RunAsync(header, cts.Token).ContinueWith(_ => Dispatcher.UIThread.Post(() =>
                {
                    if (_navHeaderPulses.TryGetValue(tabKey, out var p) && p.Cts == cts) StopNavDoorHeaderPulse(tabKey);
                }));
                return true;
            }
            catch (Exception ex)
            {
                Log.Debug("StartNavDoorHeaderPulse({Tab}): {E}", tabKey, ex.Message);
                return false;
            }
        }

        /// <summary>WPF StopNavDoorHeaderPulse (:865): release the animation, row back to rest.</summary>
        private void StopNavDoorHeaderPulse(string tabKey)
        {
            try
            {
                if (!_navHeaderPulses.Remove(tabKey, out var pulse)) return;
                pulse.Cts.Cancel();
                pulse.Cts.Dispose();
                pulse.Header.Opacity = 1.0;
            }
            catch (Exception ex) { Log.Debug("StopNavDoorHeaderPulse({Tab}): {E}", tabKey, ex.Message); }
        }
    }
}
