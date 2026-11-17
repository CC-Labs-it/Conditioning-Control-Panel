using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel
{
    /// <summary>
    /// The first-run "Start here" card (2026-11-17): where content packs come from, what to try
    /// first, where Settings and the panic key live.
    ///
    /// <para>Offered once, when the first-run show closes (it is non-modal, so the ladder cannot
    /// wait on it). It reaches the screen through the passive path: open at once on a calm screen,
    /// an Inbox row otherwise. The flag is spent when the card OPENS, so a row waved away unread
    /// does not burn the only offer. Replay lives in Help and counts nothing.</para>
    /// </summary>
    public partial class MainWindow
    {
        /// <summary>Called when the first-run show ends (or could not start).</summary>
        internal void OfferStartHereIfNeeded()
        {
            try
            {
                var s = App.Settings?.Current;
                if (s == null) return;
                if (!StartHerePlan.ShouldOffer(s.StartHereShown, s.HasAcceptedAgeVerification)) return;
                if (Dispatcher.HasShutdownStarted) return;

                Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
                    PresentOrInbox(new Services.Startup.InboxItem
                    {
                        Key = "intro:starthere",
                        Glyph = "🧭",
                        Title = Loc.Get("starthere_title"),
                        Summary = Loc.Get("starthere_intro"),
                        Open = () =>
                        {
                            var live = App.Settings?.Current;
                            if (live != null && !live.StartHereShown)
                            {
                                live.StartHereShown = true;
                                App.Settings?.Save();
                            }
                            ShowStartHereCard();
                        },
                    })));
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Start here offer failed: {E}", ex.Message);
            }
        }

        /// <summary>Opens the card (owned, non-modal).</summary>
        internal void ShowStartHereCard()
        {
            try
            {
                var card = new StartHereCard(StartHereGo) { Owner = this };
                card.Show();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Start here card failed to open");
            }
        }

        private void StartHereGo(StartHereRow row)
        {
            if (row.Link != null)
            {
                Process.Start(new ProcessStartInfo(row.Link) { UseShellExecute = true });
                return;
            }
            if (!string.IsNullOrEmpty(row.SettingsSection)) OpenAppSettingsSection(row.SettingsSection!);
            else if (!string.IsNullOrEmpty(row.Tab)) ShowTab(row.Tab!);
        }

        /// <summary>Help overlay: "Start here".</summary>
        private void BtnStartHereReplay_Click(object sender, RoutedEventArgs e)
        {
            MainTutorialOverlay.Visibility = Visibility.Collapsed;
            if (SettingsTab.BrowserContainer != null)
                SettingsTab.BrowserContainer.Visibility = Visibility.Visible;
            ShowStartHereCard();
        }
    }
}
