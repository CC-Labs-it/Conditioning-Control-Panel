// PORTED-IN-PART from ConditioningControlPanel/MainWindow/MainWindow.SubjectsFx.cs (143 lines).
//
// The hero fog is live, and half of it was already here: AvailableSubjectsTabView starts its own
// SubjectsAmbientFx on Loaded (two puffs at 0.40, the WPF tuning), because when that view was
// ported there was no tab host to start it. What was missing is the OTHER half - the tab-level
// park. Without the registration below, the roster's fog kept ticking while the user sat on a
// completely different tab, which is exactly the idle burn RegisterTabFx exists to stop.
//
// So this file registers, and deliberately does not start: the view owns the tuning constants and
// a second StartLayers from here would reseed a composition the view already made.
//
// ponytail: AvailableSubjectsTabView's Loaded handler calls StartLayers, and StartLayers clears the
// paused flag. Loaded fires once per attach, so in practice the park holds - but if that view is
// ever re-attached while another tab is showing, it un-parks itself. The canvas still self-gates
// on IsVisible, so the worst case is a composed-but-invisible surface, not a running clock.
//
// The entrance stagger now lives beside the roster in AvailableSubjectsTabView.Fx.cs.
// OnSubjectConnectPress is not ported: current WPF never calls it (Lobby frame replaced Connect).
// The view's visibility/loaded hooks replace WPF's shell
// callbacks, while this partial continues to own tab-level ambient parking.

using System;
using Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Controls;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        private bool _subjectsFxInitialized;

        /// <summary>
        /// Enrols the roster's fog in the tab-level park/resume, once, on first arrival.
        /// </summary>
        private void EnsureSubjectsFx()
        {
            if (_subjectsFxInitialized) return;
            _subjectsFxInitialized = true;
            try
            {
                // FindControl on both hops. AvailableSubjectsTabView loads with
                // AvaloniaXamlLoader.Load, so its generated SubjectsAmbientFx field is null too -
                // the x:Name hazard is not confined to this window.
                var canvas = Named<Tabs.AvailableSubjectsTabView>("AvailableSubjectsTab")
                    ?.FindControl<AmbientFxCanvas>("SubjectsAmbientFx");
                RegisterTabFx("availablesubjects", canvas);
            }
            catch (Exception ex) { Log.Warning(ex, "EnsureSubjectsFx failed"); }
        }
    }
}
