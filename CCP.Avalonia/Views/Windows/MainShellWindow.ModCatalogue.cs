using System;
using System.Threading.Tasks;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Models;
using Avalonia.Controls;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>WPF metadata-only mod share. Affirmation and the creator's MEGA URL gate the POST.</summary>
        internal async Task ShareModToCatalogueAsync(ModPackage mod, Window? owner = null)
        {
            if (mod == null || mod.IsBuiltIn || string.IsNullOrEmpty(mod.InstalledPath)) return;
            if (!CatalogueSignedIn()) return;
            var dialog = new Dialogs.AssetSubmitDialog(mod.Name, CoreAccount.DisplayName,
                requireDownloadUrl: true,
                defaultTags: mod.Manifest.Tags == null ? null : string.Join(", ", mod.Manifest.Tags));
            if (!await dialog.ShowDialogSafe<bool>(owner ?? this) || !dialog.Confirmed) return;
            JObject asset;
            try { asset = ModCatalogueAssetBuilder.Build(mod, dialog.DownloadUrl); }
            catch (Exception ex)
            {
                Log.Warning(ex, "[Catalogue] Mod asset build failed");
                App.Notifications.Show(Loc.Get("catalogue_toast_unknown_error"), Helpers.NotificationType.Error, TimeSpan.FromSeconds(8));
                return;
            }
            SubmissionResult result;
            try
            {
                result = await App.Catalogue.SubmitCatalogueAssetAsync(CatalogueKindMods, asset,
                    ModCatalogueAssetBuilder.Schema, dialog.Creator, dialog.Tags, default);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[Catalogue] Mod share threw unexpectedly");
                result = new SubmissionResult.UnknownError(0, ex.Message);
            }
            RecordCatalogueSubmission(CatalogueKindMods, mod.Id, result);
            if (result is SubmissionResult.Success)
                App.Notifications.Show(Loc.Get("catalogue_toast_mod_success"), Helpers.NotificationType.Success, TimeSpan.FromSeconds(10));
            else ShowCatalogueSubmissionResultToast(result);
        }

        /// <summary>WPF HandleModDropAsync (ModCatalogue.cs:84): read the manifest, confirm with
        /// name and author, install, toast the outcome.</summary>
        internal async Task HandleModDropAsync(string ccpmodPath)
        {
            if (App.Mods == null) return;

            var manifest = await ModService.PeekManifestAsync(ccpmodPath);
            if (manifest == null)
            {
                App.Notifications.Show(Loc.GetF("toast_mod_install_failed_fmt", Loc.Get("msg_failed_to_install_mod")),
                    Helpers.NotificationType.Error, TimeSpan.FromSeconds(8));
                return;
            }

            if (!await Dialogs.MessageDialog.ConfirmAsync(this, Loc.Get("title_install_mod"),
                    Loc.GetF("msg_confirm_install_mod_fmt", manifest.Name, manifest.Author))) return;

            var result = await App.Mods.InstallModAsync(ccpmodPath);
            if (result.Success)
                App.Notifications.Show(Loc.GetF("toast_mod_installed_fmt", manifest.Name),
                    Helpers.NotificationType.Success, TimeSpan.FromSeconds(8));
            else
                App.Notifications.Show(Loc.GetF("toast_mod_install_failed_fmt", result.ErrorMessage ?? Loc.Get("msg_failed_to_install_mod")),
                    Helpers.NotificationType.Error, TimeSpan.FromSeconds(10));
        }
    }
}
