using System;
using System.IO;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Quiz;
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

        private void InitializeIntakePassNudge()
        {
            Opened += (_, _) =>
            {
                if (_intakeNudgeScheduled || !IntakeNudgeArmed) return;
                _intakeNudgeScheduled = true;
                DispatcherTimer.RunOnce(CheckIntakePassNudge, TimeSpan.FromSeconds(14));   // WPF Marquee.cs:717
            };
        }

        internal void CheckIntakePassNudge()
        {
            try
            {
                var settings = CoreSettings.Current;
                var pass = App.IntakePass;

                if (!settings.IntakeNudgeEnabled) return;
                // Nothing to advertise: patron, signed out, or already ran it this week.
                if (!pass.IsPassAvailable) return;
                var week = IntakePassService.CurrentWeekKey();
                if (string.Equals(settings.IntakeNudgeDismissedWeek, week, StringComparison.Ordinal)) return;
                // One popup per launch, and the server's announcement outranks ours.
                if (_serverAnnouncementShownThisLaunch) return;
                // Never on top of something the user is in the middle of (WPF: session, DtRH, intake,
                // Arcademy and every WebView2 game host; here the game windows and the intake host).
                if (CoreSession.IsSessionRunning) return;
                if (Games.GameWindow.IsAnyOpen()) return;
                if (IntakeHostWindow.Snapshot().Length > 0) return;

                _serverAnnouncementShownThisLaunch = true;   // our popup now owns the slot too

                var cardArt = IntakePassCardArt();
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
                    Open = () => BuildNudge().Show(),
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

        /// <summary>WPF IntakeNiche.PassCardImage: a mod's generic pass card first, else the current
        /// niche's card; null (missing art) keeps the popup's text-only layout.</summary>
        internal static IImage? IntakePassCardArt()
        {
            try
            {
                const string generic = "intake/pass_card.png";
                if (CoreModArt.OverridePath(generic) is { } over && File.Exists(over))
                    return Helpers.ModArt.TryLoad(generic);
                var niche = IntakeRun.ResolveNiche(App.Mods?.ActiveModId, App.Mods?.ActiveMod?.Manifest?.Tags,
                    CoreSettings.Current.ContentMode == ContentMode.SissyHypno);
                return Helpers.ModArt.TryLoad($"intake/pass_card_{niche}.png");
            }
            catch { return null; }
        }
    }
}
