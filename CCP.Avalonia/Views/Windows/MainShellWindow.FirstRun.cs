// PORTED from ConditioningControlPanel/MainWindow/MainWindow.xaml.cs:539-605 - the first-launch
// branch of InitializeUI, which is the ONLY thing that ever opens the first-run wizard.
//
// WHY THIS PARTIAL EXISTS AT ALL: FirstRunWizard was a finished, rendered, fully ported view that
// nothing in this head constructed. `--render-all` drew it every build and no user could ever
// reach it, because the one line that opens it lives in the shell's constructor and had not been
// written. This file is that line plus the wait it needs.
//
// WHAT IS FAITHFUL:
//   * The gate is claimed in the CONSTRUCTOR (HookFirstRun runs from MainShellWindow's ctor),
//     exactly where WPF claims it, so the Welcomed / FirstRunAssetsPromptShown / LastSeenVersion
//     latch happens at the same instant relative to everything else on the launch path.
//   * The wizard opens LATER, once the window is actually on screen, and hands the first run back
//     if it never gets there. WPF polls IsLoaded for up to 10s for the same reason; Avalonia gives
//     us the Opened event instead, so the poll collapses into one handler.
//   * The post is at DispatcherPriority.Normal, never Loaded. WPF's comment on that line is
//     load-bearing: this app keeps the dispatcher busy enough that Loaded-priority work is starved
//     and the first-launch tour silently never started.
//
// Startup modals now share Platform.StartupLadder (WPF priorities 5 and 20). The welcome
// show remains the existing follow-up; What's New and the season recap still need their triggers.

using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>
        /// Called once from the constructor. Claims the first-run flags if this is a fresh
        /// install, and arms the one-shot that opens the wizard when the shell is on screen.
        ///
        /// <para>On a render, a --nav-check or any other head that seeded no settings service,
        /// <see cref="FirstRunWizard.ShouldRunAndClaim"/> returns false and nothing is armed -
        /// which is what keeps a modal out of a headless run and keeps CI off a real profile.</para>
        /// </summary>
        private void HookFirstRun()
        {
            try
            {
                bool fresh = FirstRunWizard.ShouldRunAndClaim();
                // Nav rework (WPF MainWindow.xaml.cs:537/589): the one-time What moved card, upgrades only.
                OfferWhatMovedIfNeeded(freshInstall: fresh);
                if (fresh) Opened += OnFirstRunShellOpened;
                // WPF's else branch (MainWindow.xaml.cs:602-612) no longer opens a mod picker:
                // mods are offered by the first-run wizard and the Mod Manager only.
                else if (CoreSettings.Service != null && CoreSettings.Current.Welcomed
                         && !CoreSettings.Current.HasAcceptedAgeVerification)
                    Opened += OnAgeGateShellOpened;
            }
            catch (Exception ex)
            {
                // A first-run screen must never be the reason a fresh install fails to start.
                Log.Warning(ex, "[FirstRun] Could not arm the first-run wizard");
            }
        }

        /// <summary>
        /// WPF App.xaml.cs:3622-3664: an install already Welcomed but never accepted (a kill
        /// mid-wizard, or a WPF settings file in that state) is asked before anything else.
        /// "Yes" reads "OK" to match this head's buttons (docs/avalonia-decisions.md).
        /// </summary>
        internal const string AgeGateBody =
            "This application contains adult content intended for users aged 18 and older.\n\n" +
            "By clicking \"OK\", you confirm that you are at least 18 years old and that viewing adult content is legal in your jurisdiction.\n\n" +
            "Do you wish to continue?";

        private void OnAgeGateShellOpened(object? sender, EventArgs e)
        {
            Opened -= OnAgeGateShellOpened;
            // WPF App.xaml.cs:3992: an abandoned slot asks the gate anyway, it never exits on its own.
            Platform.StartupLadder.EnqueueModal("age-gate", 5, _ => AskAgeGate(),
                () => Dispatcher.UIThread.Post(() => _ = AskAgeGate(), DispatcherPriority.Normal));
        }

        private async Task AskAgeGate()
        {
            var ok = IsVisible && await Dialogs.MessageDialog.ConfirmAsync(this, "Age Verification", AgeGateBody, defaultToCancel: true);
            if (!ok) { ExitWithoutBill(); return; }   // WPF App.xaml.cs:3835 Shutdown, no bill
            CoreSettings.Current.HasAcceptedAgeVerification = true;
            CoreSettings.Save();
        }

        /// <summary>Test seam: stands in for <see cref="WelcomeShow.FirstShowService.Open"/>.</summary>
        internal Func<MainShellWindow, bool> OpenWelcomeShow = shell => WelcomeShow.FirstShowService.Open(shell);

        /// <summary>WPF MainWindow.xaml.cs:546-553: the far side of the first-run wizard opens EMI's welcome
        /// show, only once the age gate was accepted and the shell is still up. Once per install: the wizard
        /// only runs when <see cref="FirstRunWizard.ShouldRunAndClaim"/> latches the Welcomed flag.</summary>
        internal void OfferWelcomeShowAfterFirstRun()
        {
            try
            {
                if (CoreSettings.Current?.HasAcceptedAgeVerification != true || !IsVisible) return;
                OpenWelcomeShow(this);
            }
            catch (Exception ex) { Log.Warning(ex, "[FirstShow] first-run show failed to open"); }
        }

        private void OnFirstRunShellOpened(object? sender, EventArgs e)
        {
            Opened -= OnFirstRunShellOpened;   // one launch, one wizard

            Platform.StartupLadder.EnqueueModal("first-run-wizard", 20, async _ =>
            {
                try
                {
                    // ShowDialog throws on an owner that is not VISIBLE, and "loaded" is not the
                    // same question. If the shell never actually made it onto the screen, hand the
                    // flags back rather than spending a first run nobody was shown.
                    if (!IsVisible)
                    {
                        FirstRunWizard.AbortUngatedLaunch(this, "shell window never became visible");
                        return;
                    }

                    await FirstRunWizard.Run(this);
                    Platform.StartupLadder.EnqueueModal("first-show", 25, _ =>
                    {
                        OfferWelcomeShowAfterFirstRun();
                        return Task.CompletedTask;
                    });
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "[FirstRun] The first-run wizard failed to run");
                }
            }, () => FirstRunWizard.AbortUngatedLaunch(this, "startup ladder never cleared"));
        }
    }
}
