using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    /// <summary>
    /// WPF MainWindow.Marquee.cs CheckIntakePassNudge / StartIntakeFromNudge (:992-1121): once a week, tell a
    /// free user their Graded Intake pass is waiting - 14 s after the shell opens, well behind the server
    /// announcement's 7 s, and never in the same launch as it (both use AnnouncementPopup). Every condition
    /// is a reason NOT to interrupt; the startup ladder then shows it or parks it as an Inbox row.
    /// </summary>
    public partial class MainShellWindow
    {
        /// <summary>Seeded true by the app's desktop startup only, so test and render shells never get a
        /// popup 14 s after they open (P52). Tests call <see cref="CheckIntakePassNudge"/> directly.</summary>
        internal static bool IntakeNudgeArmed;

        /// <summary>WPF _serverAnnouncementShownThisLaunch: this launch's one popup slot is used, by the
        /// server announcement (claimed at routing time) or by the nudge itself.</summary>
        private bool _serverAnnouncementShownThisLaunch;

        private bool _intakeNudgeScheduled;
        private IDisposable? _intakeNudgeDelay;

        private void InitializeIntakePassNudge()
        {
            Closed += (_, _) => _intakeNudgeDelay?.Dispose();
            Opened += (_, _) =>
            {
                if (_intakeNudgeScheduled || !IntakeNudgeArmed) return;
                _intakeNudgeScheduled = true;
                _intakeNudgeDelay = DispatcherTimer.RunOnce(CheckIntakePassNudge, TimeSpan.FromSeconds(14));   // WPF Marquee.cs:717
            };
        }

        internal void CheckIntakePassNudge()
        {
            try
            {
                var settings = CoreSettings.Current;
                var pass = App.IntakePass;

                var week = IntakePassService.CurrentWeekKey();
                if (!IntakeNudgeRule.ShouldOffer(settings.IntakeNudgeEnabled, pass.IsPassAvailable,
                    settings.IntakeNudgeDismissedWeek, week, _serverAnnouncementShownThisLaunch,
                    CoreSession.IsSessionRunning, Chaos.ChaosRunHost.IsActive, Games.GameWindow.IsAnyOpen(),
                    IntakeHostWindow.Snapshot().Length > 0)) return;

                _serverAnnouncementShownThisLaunch = true;   // our popup now owns the slot too

                var cardArt = IntakePassCardImage();
                var title = Loc.Get("intake_nudge_title");
                var action = Loc.Get("intake_nudge_action");

                // OUR record, never DismissedAnnouncementId (the server's single slot).
                void RecordDismissed()
                {
                    try
                    {
                        CoreSettings.Current.IntakeNudgeDismissedWeek = week;
                        CoreSettings.Save();
                    }
                    catch (Exception ex) { Log.Debug("Intake nudge dismiss: {E}", ex.Message); }
                }

                // IntakePunchCardService.UiEnabled is a static readonly false (MainShellWindow.HelpButtons.cs),
                // so the punch-free pitch is the one WPF shows: it must not promise a stamp nobody can see.
                AnnouncementPopup BuildNudge() => new(
                    $"intake-pass-{week}", title, Loc.Get("intake_nudge_pitch_nopunch"),
                    imageUrl: null, linkUrl: null, theme: null,
                    onDismiss: RecordDismissed,
                    cardImage: cardArt,
                    actionText: action,
                    onAction: StartIntakeFromNudge,
                    dismissText: Loc.Get("intake_nudge_later"));

                Platform.StartupLadder.PresentOrInbox(new Services.Startup.InboxItem
                {
                    Key = $"intake-pass:{week}",
                    Glyph = "🎟️",
                    Title = title,
                    Summary = action,
                    Open = () =>
                    {
                        // WPF Marquee.cs:1079 keeps an owner even while hidden; Avalonia refuses
                        // hidden owners. Match the existing NavSearch.cs:67 fallback at open time.
                        Window? owner = IsVisible ? this
                            : LauncherWindow.Instance is { IsVisible: true } launcher ? launcher : null;
                        var popup = BuildNudge();
                        if (owner != null) popup.Show(owner); else popup.Show();
                    },
                    Dismiss = RecordDismissed,
                });

                Log.Information("Intake pass nudge offered for {Week} (art={HasArt})", week, cardArt != null);
            }
            catch (Exception ex)
            {
                Log.Debug("CheckIntakePassNudge failed: {Error}", ex.Message);
            }
        }

        /// <summary>The nudge's call to action: the SAME entry point as the intake page's Start button, so
        /// the login / AI / pass gates stay in one place.</summary>
        private void StartIntakeFromNudge()
        {
            try { Named<Tabs.GradedIntakeTabView>("GradedIntakeTab")?.BtnStartIntake_Click(this, new RoutedEventArgs()); }
            catch (Exception ex) { Log.Warning("Intake nudge launch failed: {Error}", ex.Message); }
        }

    }
}
