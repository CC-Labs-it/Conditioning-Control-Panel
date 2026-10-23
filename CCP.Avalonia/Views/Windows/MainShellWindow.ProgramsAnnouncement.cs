using System;
using Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services.UI;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    private BeatLoop? _programsAnnouncement;
    private Button? _programsAnnouncementHeader;

    private void InitializeProgramsAnnouncement()
    {
        Opened += OnProgramsAnnouncementOpened;
        AmbientFxCanvas.Env.MotionGateChanged += OnProgramsAnnouncementMotionChanged;
        Closed += (_, _) =>
        {
            StopProgramsTabPulse();
            AmbientFxCanvas.Env.MotionGateChanged -= OnProgramsAnnouncementMotionChanged;
        };
    }

    // WPF MainWindow.xaml.cs:3818 and TabNavigation.cs:831: after 1.2 s, four 700 ms
    // sine dips (auto-reverse) on the owning rail row. ProgramsTab.cs:53 gates even Reduced.
    // ponytail: only Programs needs this on this head; reuse BeatLoop rather than importing
    // the WPF per-tab animation registry. No transform animation or private timer.
    private void OnProgramsAnnouncementOpened(object? sender, EventArgs e)
    {
        Opened -= OnProgramsAnnouncementOpened;
        if (CoreSettings.Service == null || CoreSettings.Current.HasSeenProgramsTab) return;
        if (NavAnchorForTab("programs") is not { } header) return;
        _programsAnnouncementHeader = header;
        var started = false;
        _programsAnnouncement = new BeatLoop(header, seconds =>
        {
            if (seconds < 1.2) return;
            if (!started)
            {
                if (CoreSettings.Current.HasSeenProgramsTab || StartupLadder.IsQuiet
                    || NavSections.SectionForTab(CurrentTab) == NavSections.SectionForTab("programs"))
                {
                    StopProgramsTabPulse();
                    return;
                }
                started = true;
            }
            if (!AmbientFxCanvas.Env.AllowAmbientLoops || seconds >= 1.2 + 5.6)
            {
                StopProgramsTabPulse();
                return;
            }
            header.Opacity = 1 - 0.65 * BeatLoop.Breath(seconds - 1.2, 0.7);
        });
        _programsAnnouncement.Start();
    }

    private void OnProgramsAnnouncementMotionChanged()
    {
        if (!AmbientFxCanvas.Env.AllowAmbientLoops) StopProgramsTabPulse();
    }

    internal void StopProgramsTabPulse()
    {
        Opened -= OnProgramsAnnouncementOpened;
        _programsAnnouncement?.Stop();
        _programsAnnouncement = null;
        if (_programsAnnouncementHeader is { } header) header.Opacity = 1;
        _programsAnnouncementHeader = null;
    }
}
