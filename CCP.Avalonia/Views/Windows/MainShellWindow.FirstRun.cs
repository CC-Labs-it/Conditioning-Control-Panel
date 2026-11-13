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
//     if it never gets there. The ladder waits for a visible shell and hands the first run back
//     after five minutes, matching StartupPresenter's wait and hand-back.
//   * The post is at DispatcherPriority.Normal, never Loaded. WPF's comment on that line is
//     load-bearing: this app keeps the dispatcher busy enough that Loaded-priority work is starved
//     and the first-launch tour silently never started.
//
// WHAT IS DELIBERATELY NOT PORTED:
//   * `App.EmiDesk.Fire("firstLaunchEver") / ReleaseHold(...)`. There is no EmiDesk seam in Core -
//     CCP.Core/Services/EmiDesk/ carries the book's layout and text, not the desk - so the HOLD
//     has nothing to hold back on this head. Nothing talks over the wizard here either way.
//   * `QueueEmiKnock(knockSeenVersion)`, listed as dropped in MainShellWindow.axaml.cs already.
//   * The automatic season recap modal is retired; silent bookkeeping runs below.

using System;
using Avalonia.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>
        /// Called once from the constructor. Claims the first-run flags if this is a fresh
        /// install, and queues the wizard until the shell is on screen.
        ///
        /// <para>On a render, a --nav-check or any other head that seeded no settings service,
        /// <see cref="FirstRunWizard.ShouldRunAndClaim"/> returns false and nothing is armed -
        /// which is what keeps a modal out of a headless run and keeps CI off a real profile.</para>
        /// </summary>
        private void HookFirstRun()
        {
            ProfileAdopt.SeasonAdvanced += OnSeasonAdvanced;
            Closed += (_, _) => { _seasonBookkeepingClosed = true; ProfileAdopt.SeasonAdvanced -= OnSeasonAdvanced; };
            try
            {
                bool fresh = FirstRunWizard.ShouldRunAndClaim();
                if (!fresh) UpdateSeasonBookkeeping();

                // Nav rework (WPF MainWindow.xaml.cs:537/589): the one-time What moved card, upgrades only.
                if (!fresh) ArmUpgradeIntro();
                OfferWhatMovedIfNeeded(freshInstall: fresh);
                if (fresh)
                {
                    StartupLadder.EnqueueModal("first-run-wizard", 20, async () =>
                    {
                        if (!IsVisible) { AbandonFirstRun("shell window is not visible"); return; }
                        await FirstRunWizard.Run(this);
                        if (CoreSettings.Current.HasAcceptedAgeVerification && IsVisible)
                            StartupLadder.EnqueueModal("first-show", 25, () => { OfferWelcomeShowAfterFirstRun(); return Task.CompletedTask; });
                    }, () => AbandonFirstRun("the ladder gave up on the wizard"));
                }
                // WPF's else branch (MainWindow.xaml.cs:602-612) no longer opens a mod picker:
                // mods are offered by the first-run wizard and the Mod Manager only.
                else if (CoreSettings.Service != null && CoreSettings.Current.Welcomed
                         && !CoreSettings.Current.HasAcceptedAgeVerification)
                    StartupLadder.EnqueueModal("age-gate", 5, RunAgeGateAsync,
                        () => Dispatcher.UIThread.Post(() => _ = RunAgeGateAsync(), DispatcherPriority.Normal));
            }
            catch (Exception ex)
            {
                // A first-run screen must never be the reason a fresh install fails to start.
                Log.Warning(ex, "[FirstRun] Could not arm the first-run wizard");
            }
        }

        private bool _seasonBookkeepingClosed;

        // WPF ProfileSyncService.cs:894,1029,2158 posts the nudge to the shell dispatcher.
        private void OnSeasonAdvanced(AppSettings settings)
        {
            var account = settings.UnifiedId;
            try
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (!_seasonBookkeepingClosed && ReferenceEquals(CoreSettings.Service?.Current, settings)
                        && settings.UnifiedId == account)
                        UpdateSeasonBookkeeping();
                }, DispatcherPriority.Normal);
            }
            catch (Exception ex) { Log.Warning(ex, "Failed to queue monthly bookkeeping"); }
        }

        private void UpdateSeasonBookkeeping()
        {
            try
            {
                if (CoreSettings.Service != null && SeasonRollover.UpdateBookkeeping(CoreSettings.Current,
                    handle: CoreAccount.DisplayName, isSupporter: CoreAccount.HasPremiumAccess,
                    lifetimePointsSpent: App.Achievements?.Progress?.LifetimeSkillPointsSpent ?? 0,
                    fallbackPercentile: _lbPercentile))
                    CoreSettings.Save();
            }
            catch (Exception ex) { Log.Warning(ex, "Failed to update monthly bookkeeping"); }
        }

        /// <summary>
        /// WPF App.xaml.cs:3622-3664: an install already Welcomed but never accepted (a kill
        /// mid-wizard, or a WPF settings file in that state) is asked before anything else.
        /// "Yes" reads "OK" to match this head's buttons (docs/avalonia-decisions.html).
        /// </summary>
        internal const string AgeGateBody =
            "This application contains adult content intended for users aged 18 and older.\n\n" +
            "By clicking \"OK\", you confirm that you are at least 18 years old and that viewing adult content is legal in your jurisdiction.\n\n" +
            "Do you wish to continue?";

        private void AbandonFirstRun(string why)
        {
            FirstRunWizard.HandBackFirstRun(why);
            FirstRunWizard.AbortUngatedLaunch(this, why);
        }

        private async Task RunAgeGateAsync()
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

    }
}
