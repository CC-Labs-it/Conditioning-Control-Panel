using System;
using System.Threading.Tasks;
using System.IO;
using Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Models;
using SkiaSharp;
using Serilog;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>WPF ShareModToCatalogueAsync: affirmation + MEGA link, metadata POST, status record.</summary>
        internal async Task ShareModToCatalogueAsync(ModPackage mod, Window? owner = null)
        {
            if (mod.IsBuiltIn || string.IsNullOrEmpty(mod.InstalledPath)) return;
            if (!CatalogueSignedIn()) return;
            var dialog = new AssetSubmitDialog(mod.Name, CoreAccount.DisplayName,
                requireDownloadUrl: true,
                defaultTags: mod.Manifest.Tags != null ? string.Join(", ", mod.Manifest.Tags) : null);
            if (!await dialog.ShowDialogSafe<bool>(owner ?? this) || !dialog.Confirmed) return;

            SubmissionResult result;
            try
            {
                var asset = ModCatalogueAsset.Build(mod, dialog.DownloadUrl, TryBuildModPreviewThumb(mod));
                result = await App.Catalogue.SubmitCatalogueAssetAsync(CatalogueKindMods, asset,
                    ModCatalogueAsset.Schema, dialog.Creator, dialog.Tags, default);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[Catalogue] Mod share failed");
                result = new SubmissionResult.UnknownError(0, ex.Message);
            }
            RecordCatalogueSubmission(CatalogueKindMods, mod.Id, result);
            if (result is SubmissionResult.Success)
                App.Notifications.Show(Loc.Get("catalogue_toast_mod_success"), NotificationType.Success,
                    TimeSpan.FromSeconds(10));
            else ShowCatalogueSubmissionResultToast(result);
        }

        // UI-thread-local bitmap only; no mutable Skia bitmap crosses threads.
        internal static string? TryBuildModPreviewThumb(ModPackage mod)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(mod.Manifest.PreviewImage) || mod.InstalledPath == null) return null;
                var path = Path.Combine(mod.InstalledPath, "resources", mod.Manifest.PreviewImage);
                using var codec = SKCodec.Create(path);
                if (codec == null || codec.Info.Width <= 0 || codec.Info.Height <= 0) return null;
                var scale = Math.Min(1f, 160f / codec.Info.Width);
                var size = codec.GetScaledDimensions(scale);
                using var decoded = new SKBitmap(size.Width, size.Height);
                if (codec.GetPixels(decoded.Info, decoded.GetPixels()) != SKCodecResult.Success) return null;
                // WPF DecodePixelWidth=160 always yields 160 px wide, scaling small previews up too.
                var height = Math.Max(1, (int)Math.Round(codec.Info.Height * 160d / codec.Info.Width));
                using var thumb = decoded.Resize(new SKImageInfo(160, height),
                    new SKSamplingOptions(SKFilterMode.Linear));
                if (thumb == null) return null;
                using var image = SKImage.FromBitmap(thumb);
                using var data = image.Encode(SKEncodedImageFormat.Jpeg, 80);
                return data == null || data.Size > 64 * 1024 ? null : Convert.ToBase64String(data.ToArray());
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[Catalogue] Mod preview thumb failed");
                return null;
            }
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
