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
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Startup;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Platform
{
    /// <summary>WPF App.xaml.cs:4820-4957: fresh install, two looks, one welcome sheet.
    /// Cloud settings are peeked before offering and applied only after an explicit choice.</summary>
    internal static class WelcomeBackOffer
    {
        private static bool _eligible, _hooked;
        private static int _claimed;
        private static readonly SemaphoreSlim Gate = new(1, 1);
        internal static Func<MainShellWindow?> Shell = () => MainShellWindow.Current;
        internal static Func<CloudSettingsBackup> NewBackup = () => new CloudSettingsBackup();
        internal static Func<WelcomeBackSheetContent, Task<(bool Restore, bool Flavour)>> Choose = ChooseOnShellAsync;

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

        /// <summary>WPF TryOfferWelcomeBackAsync (App.xaml.cs:4885-4957).</summary>
        internal static async Task TryOfferAsync(string source)
        {
            if (!_eligible || Volatile.Read(ref _claimed) != 0) return;
            if (!await Gate.WaitAsync(TimeSpan.FromMinutes(2))) return;
            try
            {
                if (Volatile.Read(ref _claimed) != 0 || string.IsNullOrEmpty(CoreAccount.UnifiedUserId)) return;
                var s = CoreSettings.Current;
                var client = NewBackup();
                var info = await client.GetInfoAsync(s);
                var backup = info != null ? await client.DownloadAsync(s) : null;
                var entry = ModPickerCatalog.ForMod(backup?.ActiveModId);
                var pack = entry?.PackId;
                var installed = string.IsNullOrEmpty(pack) || App.ReleaseContent == null
                    || App.ReleaseContent.IsFullInstall || App.ReleaseContent.IsInstalled(pack);
                var modName = entry == null ? null : Loc.Get(entry.NameLocKey);
                if (entry != null && (string.IsNullOrWhiteSpace(modName) || modName == entry.NameLocKey)) modName = entry.ModId;
                var plan = WelcomeBackDecision.Decide(
                    settingsFileWasMissing: true, factoryReset: false,
                    hasCloudIdentity: !string.IsNullOrEmpty(CoreAccount.UnifiedUserId),
                    backupExists: backup != null, playerLevel: s.PlayerLevel,
                    backupFlavourPackId: pack, flavourPackInstalled: installed);
                if (!plan.ShowSheet)
                {
                    Log.Information("Welcome-back ({Source}): nothing to say (level={Level})", source, s.PlayerLevel);
                    return;
                }
                if (Interlocked.CompareExchange(ref _claimed, 1, 0) != 0) return;
                Log.Information("Welcome-back ({Source}): showing the sheet", source);
                try
                {
                    var content = new WelcomeBackSheetContent
                    {
                        DisplayName = CoreAccount.DisplayName ?? "",   // WPF App.UserDisplayName (:1312) and its fallbacks
                        Level = s.PlayerLevel,
                        Plan = plan,
                        BackupModName = modName,
                        FlavourModName = plan.ShowFlavourRow ? modName : null,
                        FlavourSizeText = plan.ShowFlavourRow && entry != null
                            ? ModPickerCatalog.FormatSize(ModPickerCatalog.SizeBytesFor(entry)) : "",
                        BackupTakenAt = backup != null ? info?.BackedUpAt : null,
                        VersionLabel = CoreReleaseContent.AppVersion,
                        PatchNotes = string.Equals(backup?.LastSeenVersion, CoreReleaseContent.AppVersion, StringComparison.OrdinalIgnoreCase)
                            ? "" : CoreReleaseContent.PatchNotes,
                        SeasonLine = BuildSeasonLine(backup),
                    };
                    // Existing presentation seam still covers content tests; the real presenter returns the choice.
                    if (Present != PresentOnShellAsync) await Present(content);
                    else
                    {
                        var choice = await Choose(content);
                        if (choice.Restore && plan.ShowRestoreRow) ApplyRestore(backup);
                        if (choice.Flavour && plan.ShowFlavourRow) _ = BringFlavourAsync(backup?.ActiveModId, pack);
                    }
                }
                catch (Exception ex) { Log.Warning(ex, "Welcome-back sheet failed to show"); }
            }
            catch (Exception ex) { Log.Warning(ex, "Welcome-back offer failed"); }
            finally { Gate.Release(); }
        }

        // WPF App.xaml.cs:5183-5214: same activation/download path as the first-run wizard.
        internal static async Task BringFlavourAsync(string? modId, string? packId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(modId)) return;
                if (PendingModChoice.IsContentAvailable(modId, App.ReleaseContent))
                {
                    Shell()?.ActivateChosenMod(modId, MainShellWindow.ModChoiceTrigger.Immediate);
                    return;
                }
                if (string.IsNullOrEmpty(packId) || App.ReleaseContent == null) return;
                PendingModChoice.Record(modId, App.Mods?.ActiveModId);
                await App.ReleaseContent.RequestPackAsync(packId, null, CancellationToken.None);
            }
            catch (Exception ex) { Log.Warning(ex, "Welcome-back flavour failed"); }
        }

        // WPF App.xaml.cs:5098-5180: silent success, Inbox failure with a fresh download on retry.
        internal static void ApplyRestore(AppSettings? backup)
        {
            try
            {
                if (backup == null || CoreSettings.Service is not { } service)
                    throw new InvalidOperationException("No settings backup or service");
                CloudSettingsBackup.PrepareWelcomeBackRestore(CoreSettings.Current, backup);
                service.RestoreFrom(backup);
                App.FeatureDayLog?.Rebaseline("welcome-back restore");
                CoreSettings.Current.LastSeenVersion = CoreReleaseContent.AppVersion;
                CoreSettings.Save();
                CoreEngine.Reconcile();
                Shell()?.AppSettingsPage?.RefreshSections();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Welcome-back restore failed");
                StartupLadder.Inbox.File(new InboxItem
                {
                    Key = "welcome-back:restore-failed", Glyph = "☁",
                    Title = Loc.Get("wb_restore_failed_title"),
                    Summary = Loc.Get("wb_restore_failed_summary"),
                    Open = () => _ = RetryRestoreAsync(),
                });
            }
        }

        private static async Task RetryRestoreAsync() => ApplyRestore(await NewBackup().DownloadAsync(CoreSettings.Current));

        // WPF App.xaml.cs:5028-5053, preferring backup history. The stored current month is
        // server-confirmed (SeasonRecapService); the clock and local recap snapshots are not.
        private static string? BuildSeasonLine(AppSettings? backup)
        {
            var settings = CoreSettings.Current;
            var current = settings.CurrentSeason;
            var confirmed = !string.IsNullOrWhiteSpace(current) && ProfileAdopt.LooksLikeMonthKey(current);
            if (!WelcomeBackDecision.ShouldShowSeasonLine(current, !string.IsNullOrWhiteSpace(backup?.LastSeasonResetSeen)
                    ? backup.LastSeasonResetSeen : settings.LastSeasonResetSeen, confirmed))
                return null;
            var template = Loc.Get("wb_season_line");
            if (string.IsNullOrWhiteSpace(template) || template == "wb_season_line")
                template = "The monthly board rolled over to {0} while you were away. Your level, XP and everything you unlocked stayed.";
            try { return string.Format(template, current); }
            catch (FormatException) { return template; }
        }

        private static async Task PresentOnShellAsync(WelcomeBackSheetContent content)
        {
            await ChooseOnShellAsync(content);
        }

        private static async Task<(bool Restore, bool Flavour)> ChooseOnShellAsync(WelcomeBackSheetContent content)
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
            try
            {
                var sheet = new WelcomeBackSheet(content);
                await sheet.ShowDialogSafe<bool>(desktop?.MainWindow);
                return (sheet.RestoreChosen, sheet.BringFlavourChosen);
            }
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
            Choose = ChooseOnShellAsync;
            Shell = () => MainShellWindow.Current;
            NewBackup = () => new CloudSettingsBackup();
        }
    }
}
