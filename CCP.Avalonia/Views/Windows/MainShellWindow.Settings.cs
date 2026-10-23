// WPF MainWindow.Settings.cs shell entry points. Settings sections remain their own live
// editors; Save persists their current model and offers a preset through the existing preset page.
// Tours use TutorialHead/CoreTutorial, and folder migration uses the shared PackFolderMover.
// Save plays the tick/ripple (FlashSaveAbsorb, HeroFx) and shell tours are narrated by EMI
// (EmiTourNarrator, attached in TutorialHead.StartOnShell).
// ponytail: the book offer after a tour ending (WPF EmiCodex.MaybeOfferSoon) is wire-views-settings-emidesk-b.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // =====================================================================================
        //  navigation entry points - what every "Configure in Settings" button calls
        // =====================================================================================

        /// <summary>
        /// Opens the Settings door and scrolls to one of its sections. Goes through
        /// <see cref="ShowTab"/> so the door expansion and the per-tab FX teardown behave exactly
        /// as a rail click would. Valid keys: <see cref="Tabs.AppSettingsTabView.SectionKeys"/>;
        /// an unknown key still opens the door and simply does not scroll.
        /// </summary>
        internal void OpenAppSettingsSection(string sectionKey)
        {
            try
            {
                ShowTab("appsettings");
                AppSettingsPage?.FocusSection(sectionKey);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "OpenAppSettingsSection({Key}) failed", sectionKey);
            }
        }

        /// <summary>
        /// "Configure in Settings" from the three webcam status chips and the System panel's
        /// no-panic row. The named case of <see cref="OpenAppSettingsSection"/>, kept as its own
        /// method because ~6 call sites say "device settings" rather than a section key.
        /// </summary>
        internal void OpenDeviceSettings() => OpenAppSettingsSection("devices");

        // =====================================================================================
        //  assets folder
        // =====================================================================================

        /// <summary>
        /// Picks the custom assets folder. Async where WPF was blocking: Avalonia's picker returns
        /// a Task, so callers fire and forget (<c>_ = RequestPickAssetsFolder();</c>) the way the
        /// WPF wrappers called the click handler.
        /// </summary>
        internal Task RequestPickAssetsFolder() => PickAssetsFolder(this);

        /// <summary>
        /// The picker behind <see cref="RequestPickAssetsFolder"/>, owned by <paramref name="owner"/>
        /// (the shell, or the first-run wizard). Returns the folder it applied, or null.
        /// </summary>
        internal static async Task<string?> PickAssetsFolder(global::Avalonia.Controls.Window owner)
        {
            try
            {
                var storage = owner.StorageProvider;
                var oldAssets = CorePaths.EffectiveAssets;
                var current = CoreSettings.Current.CustomAssetsPath;
                var start = !string.IsNullOrWhiteSpace(current) && Directory.Exists(current)
                    ? current
                    : CorePaths.EffectiveAssets;

                var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = "Select a folder for your custom assets (images and videos)",
                    AllowMultiple = false,
                    SuggestedStartLocation = await storage.TryGetFolderFromPathAsync(start),
                });
                if (folders.Count != 1 || folders[0].TryGetLocalPath() is not { } selected) return null;

                var defaults = (global::Avalonia.Application.Current as App)?.DefaultAssetsRoot
                    ?? Path.Combine(CorePaths.UserData, "assets");
                return await ApplyAssetsFolder(owner, selected, oldAssets, defaults);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "RequestPickAssetsFolder failed");
                return null;
            }
        }

        /// <summary>The chosen-folder half of the OS picker; tests supply only scratch roots.</summary>
        internal static async Task<string?> ApplyAssetsFolder(global::Avalonia.Controls.Window owner,
            string selected, string oldAssets, string defaultAssets)
        {
            // #1053: scans and writes must never target a personal folder or a whole drive.
            if (IsPersonalFolderRoot(selected))
            {
                await MessageDialog.ShowAsync(owner, "Pick a folder of your own",
                    "That folder is one of your system's own - your Desktop, Documents, " +
                    "Pictures, Downloads, your home folder or a whole drive." + Environment.NewLine + Environment.NewLine +
                    "The app both reads and writes here, so pick or make a folder that holds nothing but your assets.");
                return null;
            }

            var packs = PackFolderMover.Find(oldAssets, defaultAssets, selected);
            var bytes = packs.Sum(p => p.Bytes);
            var move = packs.Count > 0 && await MessageDialog.ConfirmAsync(owner,
                Loc.Get("title_move_downloaded_packs"), Loc.GetF("msg_move_packs_confirm", packs.Count,
                    PackFolderMover.FormatSize(bytes), string.Join("\n• ", packs.Select(p => p.PackName)),
                    bytes > 500_000_000 ? ConditioningControlPanel.Avalonia.Controls.IconText.BareLoc("msg_may_take_a_moment") : ""));
            Directory.CreateDirectory(Path.Combine(selected, "images"));
            Directory.CreateDirectory(Path.Combine(selected, "videos"));
            var moved = false;
            if (move)
            {
                try { PackFolderMover.Move(packs, selected, CoreSettings.Current); moved = true; }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to move packs to new location");
                    await MessageDialog.ShowAsync(owner, Loc.Get("label_warning"), Loc.GetF("msg_could_not_move_packs_0", ex.Message));
                }
            }
            CoreSettings.Current.CustomAssetsPath = selected;
            CoreSettings.Save();
            ContentPackStore.Current?.Rescan();
            // Flash reads the folder on every burst; the dealt video queues need an explicit reset.
            Overlays.MandatoryVideoOverlay.Instance.Scheduler.ReloadAssets();
            BubbleCountHost.Instance.Scheduler.ReloadAssets();
            if (owner is MainShellWindow shell)
            {
                shell.Named<Tabs.AssetsTabView>("AssetsTab")?.RefreshAssetTree();
                shell.Named<Tabs.AssetsTabView>("AssetsTab")?.PaintAssetsFolder();
            }
            Log.Information("Custom assets path set to: {Path}", selected);
            await MessageDialog.ShowAsync(owner, Loc.Get("title_assets_folder_set"),
                Loc.GetF("msg_custom_assets_folder_set_0", selected) + (moved ? "\n\n" + Loc.Get("msg_packs_have_been_moved") : ""));
            return selected;
        }

        internal static bool IsPersonalFolderRoot(string? directory)
            => AssetFolderSafety.IsPersonalFolderRoot(directory);

        // =====================================================================================
        //  the "?" panel - MainTutorialOverlay
        // =====================================================================================

        /// <summary>Opens the help panel.</summary>
        private void BtnMainHelp_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
            => SetTutorialOverlay(true);

        /// <summary>Closes the help panel. Bound to both the scrim's PointerPressed and the ✕
        /// button's Click, which is why it takes the base RoutedEventArgs.</summary>
        private void MainTutorial_Close(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
            => CloseTutorialOverlay();

        /// <summary>Clicking the card must not close the panel behind it.</summary>
        private void MainTutorial_ContentClick(object? sender, global::Avalonia.Input.PointerPressedEventArgs e)
            => e.Handled = true;

        private void CloseTutorialOverlay() => SetTutorialOverlay(false);

        /// <summary>
        /// Both controls are resolved by name, never through the generated fields - this window
        /// loads with AvaloniaXamlLoader.Load, which leaves every one of them null (see the header
        /// of MainShellWindow.TabNavigation.cs). BrowserContainer belongs to SettingsTabView, which
        /// does call InitializeComponent, so its own field is real once the view itself is found.
        /// </summary>
        internal void SetTutorialOverlay(bool open)
        {
            var overlay = Named<global::Avalonia.Controls.Control>("MainTutorialOverlay");
            if (overlay != null) overlay.IsVisible = open;
            // WPF hides the browser first because WebView2 ignores WPF z-order. This head has no
            // WebView2 yet, but it is the same control and the panel behaves identically.
            var browser = Named<Tabs.SettingsTabView>("SettingsTab")?.BrowserContainer;
            if (browser != null) browser.IsVisible = !open;
        }

        private void StartHelpTutorial(string tour)
        {
            CloseTutorialOverlay();
            CoreTutorial.Start(tour);
        }

        private void BtnStartTutorial_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) => StartHelpTutorial("FullTour");
        private void BtnTutorialGettingStarted_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) => StartHelpTutorial("GettingStarted");
        private void BtnTutorialSettings_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) => StartHelpTutorial("Settings");
        private void BtnTutorialPresets_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) => StartHelpTutorial("Presets");
        private void BtnTutorialProgression_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) => StartHelpTutorial("Progression");
        private void BtnTutorialAchievements_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) => StartHelpTutorial("Achievements");
        private void BtnTutorialCompanion_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) => StartHelpTutorial("Companion");
        private void BtnTutorialPatreon_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) => StartHelpTutorial("Patreon");
        private void BtnTutorialAvatar_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) => StartHelpTutorial("Avatar");
        private void BtnTutorialAwareness_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            CloseTutorialOverlay();
            Named<Tabs.AwarenessTabView>("AwarenessTab")?.StartAwarenessTutorial();
        }

        private void BtnTutorialModding_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            CloseTutorialOverlay();
            new ModCreatorWindow(startWithTutorial: true).Show(this);
        }

        /// <summary>Close the help panel first, then open the owned report dialog, matching WPF.</summary>
        private void BtnTutorialReportBug_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            CloseTutorialOverlay();
            OpenBugReportWindow();
        }

        /// <summary>Open the real owned bug-report modal from the title bar.</summary>
        private void BtnReportBug_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
            => OpenBugReportWindow();

        private async void OpenBugReportWindow()
        {
            try
            {
                if (!IsVisible) return;
                await new BugReportWindow(ReportKind.Bug).ShowDialogSafe(this);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to open BugReportWindow");
                if (IsVisible)
                {
                    await MessageDialog.ShowAsync(this, Loc.Get("bug_report_title"),
                        Loc.Get("bug_report_error_toast") + "\n\n" + ex.Message);
                }
            }
        }

        private async void BtnSave_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            // WPF MainWindow.Settings.cs:381: the tick + ripple first, on its own clock.
            FlashSaveAbsorb();
            await SaveSettingsAndOfferPreset();
        }
    }
}
