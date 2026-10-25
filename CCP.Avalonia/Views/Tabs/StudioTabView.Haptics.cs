using System;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    /// <summary>
    /// The rack's <b>haptics</b> module, PORTED from WPF <c>StudioTabView.Haptics.cs</c>: the
    /// Haptics page is hosted whole (<see cref="HapticsPanel"/>), and this partial live-wires its
    /// row's state dot.
    /// </summary>
    public partial class StudioTabView
    {
        private bool _hapticsModuleHooked;

        /// <summary>
        /// The haptics enable lives on the nested <c>AppSettings.Haptics</c> object, which raises
        /// no INPC of its own, so the shared settings listener never sees it move. Hook the page's
        /// master box instead, additively: the page owns the real handler (premium gate and its
        /// revert included). Once-only, from Loaded.
        /// </summary>
        private void HookHapticsModule()
        {
            if (_hapticsModuleHooked) return;
            try
            {
                var chk = PanelHaptics?.ChkHapticsEnabled;
                if (chk == null) return;
                chk.IsCheckedChanged += OnHapticsEnabledToggled;
                _hapticsModuleHooked = true;
            }
            catch (Exception ex) { Log.Debug(ex, "[Studio] haptics module hook failed"); }
        }

        /// <summary>One beat late at Normal priority, so a handler that vetoes the toggle and
        /// reverts IsChecked has already run when the dot reads the value.</summary>
        private void OnHapticsEnabledToggled(object? sender, RoutedEventArgs e)
        {
            try { Dispatcher.UIThread.Post(RefreshDots, DispatcherPriority.Normal); }
            catch { /* a state dot must never break a toggle */ }
        }
    }
}
