using System;
using System.Windows;
using System.Windows.Threading;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel
{
    /// <summary>
    /// The assets nudge (2026-11-25, owner's order after a new member in #general could not find
    /// how to get pictures in or switch the Scrolller ones on): three small cards, each with a
    /// moving arrow on the real control. 1) the Library source bar, 2) the Folders zone (and drag
    /// and drop), 3) the Start button on Home.
    ///
    /// <para>Offered once per profile through the passive path (open on a calm screen, an Inbox
    /// row otherwise): on a fresh install when the Start here card closes, on an upgrade at launch
    /// only when the library is still empty. The flag is spent when the cards OPEN. Replay lives
    /// in Help and counts nothing. Lockdown and Strict Lock never see it.</para>
    /// </summary>
    public partial class MainWindow
    {
        private AssetsNudgeCard? _assetsNudgeCard;

        /// <summary>Fresh install: called when Start here closes. Upgrade: called at launch.</summary>
        internal void OfferAssetsNudgeIfNeeded(bool freshInstall)
        {
            try
            {
                var s = App.Settings?.Current;
                if (s == null || Dispatcher.HasShutdownStarted) return;
                if (!AssetsNudgePlan.ShouldOffer(s.AssetsNudgeShown, s.HasAcceptedAgeVerification, freshInstall,
                        Services.EmiDesk.EmiOffers.LibraryIsEmpty(), App.Lockdown?.IsActive == true, s.StrictLockEnabled))
                    return;

                Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
                    PresentOrInbox(new Services.Startup.InboxItem
                    {
                        Key = "intro:assetsnudge",
                        Glyph = "🖼️",
                        Title = Loc.Get("assetsnudge_inbox_title"),
                        Summary = Loc.Get("assetsnudge_inbox_summary"),
                        Open = () =>
                        {
                            var live = App.Settings?.Current;
                            if (live != null && !live.AssetsNudgeShown)
                            {
                                live.AssetsNudgeShown = true;
                                App.Settings?.Save();
                            }
                            ShowAssetsNudge();
                        },
                    })));
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Assets nudge offer failed: {E}", ex.Message);
            }
        }

        /// <summary>Opens the cards (owned, non-modal, never activated). One at a time.</summary>
        internal void ShowAssetsNudge()
        {
            try
            {
                if (App.Lockdown?.IsActive == true || App.Settings?.Current?.StrictLockEnabled == true) return;
                _assetsNudgeCard?.Close();
                var motion = App.Settings?.Current?.MotionLevel ?? Models.MotionLevel.Full;
                var card = new AssetsNudgeCard(AssetsNudgePlan.Steps, ResolveAssetsNudgeTarget,
                    AssetsNudgePlan.ArrowMoves(motion, SystemParameters.ClientAreaAnimation)) { Owner = this };
                card.Closed += (_, _) => { if (ReferenceEquals(_assetsNudgeCard, card)) _assetsNudgeCard = null; };
                _assetsNudgeCard = card;
                card.Show();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Assets nudge failed to open");
            }
        }

        /// <summary>Goes to the step's tab and hands back the control the arrow points at.</summary>
        private FrameworkElement? ResolveAssetsNudgeTarget(AssetsNudgeStep step)
        {
            // A Lockdown that started while the cards were up ends them: nothing navigates under it.
            if (App.Lockdown?.IsActive == true)
            {
                _assetsNudgeCard?.Close();
                return null;
            }
            ShowTab(step.Tab);
            return step.Target switch
            {
                AssetsNudgePlan.TargetSourceBar => AssetsTab?.RemoteSourceChips,
                AssetsNudgePlan.TargetFolders => AssetsTab?.ZoneFolders,
                AssetsNudgePlan.TargetStart => BtnStart,
                _ => null,
            };
        }

        /// <summary>Help overlay: "Getting pictures in".</summary>
        private void BtnAssetsNudgeReplay_Click(object sender, RoutedEventArgs e)
        {
            MainTutorialOverlay.Visibility = Visibility.Collapsed;
            if (SettingsTab.BrowserContainer != null)
                SettingsTab.BrowserContainer.Visibility = Visibility.Visible;
            ShowAssetsNudge();
        }
    }
}
