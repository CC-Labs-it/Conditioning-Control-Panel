using System;
using System.Threading.Tasks;
using System.Windows;
using Newtonsoft.Json.Linq;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel
{
    // Share an installed mod to the web catalogue. Unlike presets/sessions the
    // binary is NOT uploaded — mods are 100+ MB, so the creator hosts the .ccpmod
    // on MEGA and the catalogue stores metadata + the download link (ccp-mod/v1).
    public partial class MainWindow
    {

        internal async Task ShareModToCatalogueAsync(ModPackage mod, Window? owner = null)
        {
            if (mod == null || mod.IsBuiltIn || string.IsNullOrEmpty(mod.InstalledPath)) return;

            if (string.IsNullOrEmpty(App.Settings?.Current?.AuthToken) || App.Catalogue == null)
            {
                App.Notifications?.Show(Loc.Get("catalogue_toast_auth_failed"),
                    Services.NotificationType.Warning, TimeSpan.FromSeconds(8));
                return;
            }

            var dialog = new AssetSubmitDialog(
                mod.Name, App.UserDisplayName,
                requireDownloadUrl: true,
                defaultTags: mod.Manifest.Tags != null ? string.Join(", ", mod.Manifest.Tags) : null)
            { Owner = owner ?? this };
            if (dialog.ShowDialog() != true || !dialog.Confirmed) return;

            JObject asset;
            try { asset = BuildModCatalogueAsset(mod, dialog.DownloadUrl); }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "[Catalogue] Mod asset build failed");
                App.Notifications?.Show(Loc.Get("catalogue_toast_unknown_error"),
                    Services.NotificationType.Error, TimeSpan.FromSeconds(8));
                return;
            }

            SubmissionResult result;
            try
            {
                result = await App.Catalogue.SubmitCatalogueAssetAsync(
                    CatalogueKindMods, asset, ModCatalogueAssetBuilder.Schema,
                    dialog.Creator, dialog.Tags, default).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "[Catalogue] Mod share threw unexpectedly");
                result = new SubmissionResult.UnknownError(0, ex.Message);
            }

            RecordCatalogueSubmission(CatalogueKindMods, mod.Id, result);

            // Mods get a dedicated success toast that says where the mod ends
            // up (the web catalogue's Mods tab) and reminds the creator the
            // .ccpmod stays hosted on THEIR MEGA - we only list the link.
            if (result is SubmissionResult.Success)
            {
                App.Notifications?.Show(Loc.Get("catalogue_toast_mod_success"),
                    Services.NotificationType.Success, TimeSpan.FromSeconds(10));
            }
            else
            {
                ShowCatalogueSubmissionResultToast(result);
            }
        }

        // Drag-drop install: a .ccpmod downloaded from the web catalogue (MEGA)
        // dropped onto the main window. Installing a mod is a trust decision, so
        // peek the manifest and confirm name/author before touching anything.
        private async Task HandleModDropAsync(string ccpmodPath)
        {
            if (App.Mods == null) return;

            var manifest = await ModService.PeekManifestAsync(ccpmodPath);
            if (manifest == null)
            {
                App.Notifications?.Show(Loc.GetF("toast_mod_install_failed_fmt",
                    Loc.Get("msg_failed_to_install_mod")),
                    Services.NotificationType.Error, TimeSpan.FromSeconds(8));
                return;
            }

            var confirm = MessageBox.Show(this,
                Loc.GetF("msg_confirm_install_mod_fmt", manifest.Name, manifest.Author),
                Loc.Get("title_install_mod"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;

            var result = await App.Mods.InstallModAsync(ccpmodPath);
            if (result.Success)
            {
                App.Notifications?.Show(Loc.GetF("toast_mod_installed_fmt", manifest.Name),
                    Services.NotificationType.Success, TimeSpan.FromSeconds(8));
            }
            else
            {
                App.Notifications?.Show(Loc.GetF("toast_mod_install_failed_fmt",
                    result.ErrorMessage ?? Loc.Get("msg_failed_to_install_mod")),
                    Services.NotificationType.Error, TimeSpan.FromSeconds(10));
            }
        }

        // Shared metadata/preview behavior: both heads submit exactly the same envelope.
        private static JObject BuildModCatalogueAsset(ModPackage mod, string downloadUrl)
            => ModCatalogueAssetBuilder.Build(mod, downloadUrl);
    }
}
