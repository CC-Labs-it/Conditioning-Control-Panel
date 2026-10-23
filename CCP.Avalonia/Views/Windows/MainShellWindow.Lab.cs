// PARTLY PORTED from ConditioningControlPanel/MainWindow/MainWindow.Lab.cs (1354 lines) - the
// lockdown partial, and the launcher for every Lab activity.
//
// The lockdown half no longer blocks LockdownTabView (its lifecycle moved there, see below); what
// remains here is the Lab launcher. The two halves do not have the same shape:
//
//   THE ONE MEMBER THAT WAS NEVER BLOCKED is LockdownBadge_Click, restored below. The badge is a
//   signpost, not a control: it navigates to the Lockdown page and cannot end anything. ShowTab is
//   real here, so the badge in the title bar now leads somewhere instead of swallowing the click.
//
//   THE LOCKDOWN LIFECYCLE lives on this head in LockdownTabView.axaml.cs (Activate, panel swap,
//   clock, exit phrase) and MainShellWindow.Lockdown.cs (refusals), driven by Core LockdownService.
//   The title-bar badge's visibility and clock live in MainShellWindow.Lockdown.cs.
//   Theme, activation flash and the system-key hook now live in MainShellWindow.Lockdown.cs +
//   Platform/Win32Input.cs; the card theme lives in LockdownTabView. The level-chip glow and
//   temporary visual cleanup on close are covered by LabLockdownThemeTests.
//
//   THE POP-QUIZ AND GRADED-INTAKE EDITORS ARE NOT THIS FILE'S ANY MORE. WPF's MainWindow owned
//   ChkPopQuizEnabled_Changed / SliderPopQuizFrequency_ValueChanged / BtnTestPopQuiz_Click because
//   the intake markup was inline in MainWindow.xaml. The port moved that markup to
//   CCP.Avalonia/Views/Tabs/GradedIntakeTabView.axaml, whose Click=/Changed= handlers name THAT
//   view (GradedIntakeTabView.axaml.cs:36-38, still stubs). The first two are one CoreSettings
//   write each - PopQuizEnabled and PopQuizFrequency are both in Core, and the slider needs the
//   `_isLoading` guard because Avalonia raises ValueChanged on a programmatic set too. Restoring
//   them HERE would be a second copy no click can reach; they belong in that view's layer.
//   RefreshGradedIntakeGate / SetGradedIntakeGateCopy / EnsureIntakePassHooked / BtnStartIntake_Click
//   live in GradedIntakeTabView now (IntakePassService moved to Core); only the launch
//   (IntakeHostService) is still missing.
//
//   THE ACTIVITY LAUNCHERS are all "new SomeWindow(...).Show()" over a service: quiz, intake,
//   bureau, goon, chaos, arcademy, the FYP feed. Windows and services both, so both blocked -
//   except the quiz: BtnStartQuiz_Click and RefreshPastQuizzes live in GradedIntakeTabView.
//
// Members dropped (47 - the one now restored is marked RESTORED):
//   private void InitializeLockdown(…)
//   internal void BtnActivateLockdown_Click(…)
//   internal void BtnStartQuiz_Click(…)          -> GradedIntakeTabView
//   internal void BtnStartIntake_Click(…) -> GradedIntakeTabView
//   internal void BtnStartBureau_Click(…)
//   internal void BtnStartGoon_Click(…)
//   internal void BtnStartChaos_Click(…)
//   internal void OpenFypFeed(…)
//   internal void BtnStartArcademy_Click(…)
//   internal void BtnQuickStartChaos_Click(…)
//   private bool _intakePassHooked
//   internal void RefreshGradedIntakeGate(…) -> GradedIntakeTabView
//   private void SetGradedIntakeGateCopy(…) -> GradedIntakeTabView
//   private void EnsureIntakePassHooked(…) -> GradedIntakeTabView
//   private void OnIntakePassStateChanged(…) -> GradedIntakeTabView
//   private void RefreshPastQuizzes(…)          -> GradedIntakeTabView
//   internal void ChkPopQuizEnabled_Changed(…)
//   internal void SliderPopQuizFrequency_ValueChanged(…)
//   internal void BtnTestPopQuiz_Click(…)
//   private void OnLockdownActivated(…)
//   private void OnLockdownDeactivated(…)
//   private void OnLockdownTick(…)
//   private static string FormatLockdownClock(…)
//   private void SetLockdownBadge(…)                 -> MainShellWindow.Lockdown.cs
//   private void LockdownBadge_Click(…)                RESTORED
//   internal void TxtLockdownTimer_Click(…)
//   internal void TxtLockdownExit_KeyDown(…)
//   private Action<PossessionRung>? _possessionRungHandler RESTORED (LockdownTabView)
//   private static readonly Color PossessionEmber RESTORED (LockdownTabView)
//   private static readonly Color PossessionEmberDim RESTORED (LockdownTabView)
//   private void HookPossessionReadout(…) RESTORED -> LockdownTabView.PaintPossessionReadout + InitializePossessionReadout
//   private void DetachPossessionRungHandler(…) RESTORED -> LockdownTabView.PaintPossessionReadout + InitializePossessionReadout
//   private void UnhookPossessionReadout(…) RESTORED -> LockdownTabView.PaintPossessionReadout + InitializePossessionReadout
//   private void UpdatePossessionReadout(…) RESTORED -> LockdownTabView.PaintPossessionReadout + InitializePossessionReadout
//   private Action<string>? _lockdownRestartHandler
//   private void HookLockdownRestart(…) RESTORED -> LockdownTabView TimerRestarted + MainShellWindow.Lockdown.cs
//   private void UnhookLockdownRestart(…) RESTORED -> LockdownTabView TimerRestarted + MainShellWindow.Lockdown.cs
//   private void OnLockdownTimerRestarted(…) RESTORED -> LockdownTabView TimerRestarted + MainShellWindow.Lockdown.cs
//   internal void PreviewShowLockdownActivePanel(…)
//   private void ShowPossessionRulesIfFirstTime(…) RESTORED
//   private static readonly Color LockdownCrimson
//   private static readonly Color LockdownDarkRed
//   private static readonly Color LockdownPanelBg
//   private static readonly Color LockdownWindowBg
//   private void ApplyLockdownTheme(…)
//   private void RestoreLockdownTheme(…)
//   private void PlayLockdownActivationAnimation(…)

using System;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Possession;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>
        /// The crimson pill in the title bar's status row. It is a signpost, not a button that
        /// ends anything: it navigates to the Lockdown page, where the Emergency Exit and the
        /// secret phrase both live. Nothing here can end a lockdown, which is what lets it sit one
        /// pixel from the window's close button.
        ///
        /// <para><c>e.Handled = true</c> is kept for the same reason WPF sets it: the badge sits
        /// inside the draggable title bar, and an unhandled press would start a window drag under
        /// the finger that just navigated.</para>
        ///
        /// <para>Visibility and clock: InitializeLockdownGreys (MainShellWindow.Lockdown.cs).</para>
        /// </summary>
        private void LockdownBadge_Click(object? sender, global::Avalonia.Input.PointerPressedEventArgs e)
        {
            try
            {
                e.Handled = true;
                ShowTab("lockdown");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Lockdown: badge navigation failed");
            }
        }

        // --- Possession readout + first-run rules (WPF Lab.cs:867-986, 1100) ---------------
        // LockdownTabView paints the readout (Settle at activation, cleared at exit, Settle on a timer
        // restart); the shell owns the director, so the rung MOVES are relayed from here.

        private void InitializePossessionReadout()
        {
            void Rung(PossessionRung rung) => Dispatcher.UIThread.Post(() =>
            {
                // WPF's handler only exists between HookPossessionReadout and UnhookPossessionReadout.
                if (_possessionClosed || !LockdownActive || !CoreSettings.Current.LockdownPossessionEnabled) return;
                Named<LockdownTabView>("LockdownTab")?.PaintPossessionReadout(rung);
            });
            void Activated() => Dispatcher.UIThread.Post(() => { if (!_possessionClosed) ShowPossessionRulesIfFirstTime(); });
            if (PossessionDirector is { } director) director.RungChanged += Rung;
            if (LockdownService.Current is { } ld) ld.LockdownActivated += Activated;
            Closed += (_, _) =>
            {
                if (PossessionDirector is { } d) d.RungChanged -= Rung;
                if (LockdownService.Current is { } l) l.LockdownActivated -= Activated;
            };
        }

        /// <summary>WPF ShowPossessionRulesIfFirstTime (Lab.cs:1100): card AND bark on the first armed
        /// lockdown. Through the presenter, so a quiet window makes it an Inbox row; the flag is spent
        /// when the card OPENS, never when the row is posted.</summary>
        internal void ShowPossessionRulesIfFirstTime()
        {
            try
            {
                var s = CoreSettings.Current;
                if (!s.LockdownPossessionEnabled || s.LockdownPossessionIntroSeen) return;
                Platform.StartupLadder.PresentOrInbox(new Services.Startup.InboxItem
                {
                    Key = "intro:possession",
                    // WPF's row glyph is an emoji; the icon guard forbids a new one, so the row reuses
                    // the possession intro card's own glyph.
                    Glyph = FeatureIntros.All["possession"].Glyph,
                    Title = "The warden's rules",
                    Summary = "What possession does before the room starts moving.",
                    Open = () =>
                    {
                        var live = CoreSettings.Current;
                        if (!live.LockdownPossessionIntroSeen)
                        {
                            live.LockdownPossessionIntroSeen = true;
                            CoreSettings.Save();
                        }
                        FeatureIntroPopup.ShowIfFirstTime("possession", this);
                    },
                });
                Platform.BarkHead.Engine?.Raise(PossessionBarkTriggers.Rules, guaranteed: true);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Possession: failed to show the first-run rules");
            }
        }
    }
}
