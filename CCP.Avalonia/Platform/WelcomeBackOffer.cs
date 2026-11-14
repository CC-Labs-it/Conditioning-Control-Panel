using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Serilog;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Platform
{
    /// <summary>
    /// WPF's welcome-back trigger (App.xaml.cs:4600-4760): a fresh settings file that is not a factory
    /// reset arms it, and two looks decide - one after startup's session validation (WPF's 5 s wait for
    /// provider auth, App.xaml.cs:4647) and one when the profile loads (ProfileSync.ProfileLoaded,
    /// :4660). Whichever gets there first spends the launch's one sheet.
    /// <para>The backup half is not on this head (decision 2026-10-09, rows-cloud-remember), so the
    /// rule runs with <c>backupExists: false</c>: no restore or flavour row, and the sheet greets a
    /// returning account from level 2 (<see cref="WelcomeBackDecision.GreetWithoutBackupFromLevel"/>).
    /// Release notes and the confirmed season rotation share the same sheet.</para>
    /// </summary>
    internal static class WelcomeBackOffer
    {
        private static bool _eligible, _hooked;
        private static int _claimed;

        /// <summary>Presents the sheet; tests replace it. Default: modal on the shell, after the wizard.</summary>
        internal static Func<WelcomeBackSheetContent, Task> Present = PresentOnShellAsync;

        /// <summary>WPF CheckCloudSettingsRestoreAsync (:4626) up to the wait.</summary>
        internal static void Arm(bool settingsFileWasMissing, string userDataDir)
        {
            if (!settingsFileWasMissing) return;
            if (ConsumeFactoryResetMarker(userDataDir))
            {
                Log.Information("Skipping the welcome-back sheet - the missing settings file is a factory reset");
                return;
            }
            _eligible = true;
            if (_hooked) return;
            _hooked = true;
            AccountSeed.ProfileLoaded += OnProfileLoaded;   // armed before the first look (WPF :4644)
        }

        private static void OnProfileLoaded()
        {
            AccountSeed.ProfileLoaded -= OnProfileLoaded;   // the first load is the arrival (WPF :4669)
            _ = TryOfferAsync("profile loaded");
        }

        /// <summary>WPF TryOfferWelcomeBackAsync (:4690), backup-less.</summary>
        internal static async Task TryOfferAsync(string source)
        {
            if (!_eligible || Volatile.Read(ref _claimed) != 0) return;
            var s = CoreSettings.Current;
            var plan = WelcomeBackDecision.Decide(
                settingsFileWasMissing: true, factoryReset: false,
                hasCloudIdentity: !string.IsNullOrEmpty(CoreAccount.UnifiedUserId),
                backupExists: false, playerLevel: s.PlayerLevel,
                backupFlavourPackId: null, flavourPackInstalled: true);
            if (!plan.ShowSheet)
            {
                Log.Information("Welcome-back ({Source}): nothing to say (level={Level})", source, s.PlayerLevel);
                return;
            }
            if (Interlocked.CompareExchange(ref _claimed, 1, 0) != 0) return;
            Log.Information("Welcome-back ({Source}): showing the sheet", source);
            try
            {
                await Present(new WelcomeBackSheetContent
                {
                    DisplayName = CoreAccount.DisplayName ?? "",   // WPF App.UserDisplayName (:1312) and its fallbacks
                    Level = s.PlayerLevel,
                    Plan = plan,
                    VersionLabel = CoreReleaseContent.AppVersion,
                    PatchNotes = CoreReleaseContent.PatchNotes,
                    SeasonLine = BuildSeasonLine(),
                });
            }
            catch (Exception ex) { Log.Warning(ex, "Welcome-back sheet failed to show"); }
        }

        // WPF App.xaml.cs:5028-5053, without a backup. The stored current month is
        // server-confirmed (SeasonRecapService); the clock and local recap snapshots are not.
        private static string? BuildSeasonLine()
        {
            var settings = CoreSettings.Current;
            var current = settings.CurrentSeason;
            var confirmed = !string.IsNullOrWhiteSpace(current) && ProfileAdopt.LooksLikeMonthKey(current);
            if (!WelcomeBackDecision.ShouldShowSeasonLine(current, settings.LastSeasonResetSeen, confirmed))
                return null;
            var template = Loc.Get("wb_season_line");
            if (string.IsNullOrWhiteSpace(template) || template == "wb_season_line")
                template = "The monthly board rolled over to {0} while you were away. Your level, XP and everything you unlocked stayed.";
            try { return string.Format(template, current); }
            catch (FormatException) { return template; }
        }

        private static async Task PresentOnShellAsync(WelcomeBackSheetContent content)
        {
            var desktop = global::Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
            // The ladder runs one startup modal at a time (WPF EnqueueModal :4748): wait out the wizard.
            while (desktop?.Windows.FirstOrDefault(w => w is FirstRunWizard && w.IsVisible) is { } wizard)
            {
                var closed = new TaskCompletionSource();
                wizard.Closed += (_, _) => closed.TrySetResult();
                await closed.Task;
            }
            // WPF StartupPresenter.cs:572-587: the flag every startup surface (feature intros) defers to.
            MainShellWindow.IsStartupDialogShowing = true;
            try { await new WelcomeBackSheet(content).ShowDialogSafe<bool>(desktop?.MainWindow); }
            finally { MainShellWindow.IsStartupDialogShowing = false; }
        }

        /// <summary>WPF ConsumeFactoryResetMarker (:5033); the marker DataSettingsSection drops.</summary>
        private static bool ConsumeFactoryResetMarker(string userDataDir)
        {
            var marker = Path.Combine(userDataDir, "settings.json.factory-reset");
            if (!File.Exists(marker)) return false;
            try { File.Delete(marker); }
            catch (Exception ex) { Log.Warning("Could not clear the factory-reset marker: {Error}", ex.Message); }
            return true;
        }

        /// <summary>Test seam: forget this launch's arming and claim.</summary>
        internal static void ResetForTests()
        {
            AccountSeed.ProfileLoaded -= OnProfileLoaded;
            _eligible = _hooked = false;
            _claimed = 0;
            Present = PresentOnShellAsync;
        }
    }
}
