// PORTED from ConditioningControlPanel/MainWindow/MainWindow.ModCatalogue.cs (191 lines).
//
// Share an installed mod to the web catalogue. Unlike presets/sessions the binary is NOT uploaded -
// mods are 100+ MB, so the creator hosts the .ccpmod on MEGA and the catalogue stores metadata +
// the download link (ccp-mod/v1). Opened from ModManagerDialog's Share button (WPF BtnShare_Click).
//
// Deviation: WPF's preview thumb is BitmapImage(DecodePixelWidth=160) + JpegBitmapEncoder(80); here
// SkiaSharp (already used by EnhancementsTabView) decodes, scales to 160 px wide and encodes JPEG 80.

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Newtonsoft.Json.Linq;
using Serilog;
using SkiaSharp;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        private const string CatalogueSchemaMod = "ccp-mod/v1";

        // Preview thumb limits: keep the JSON envelope small.
        private const int ModPreviewMaxPixels = 160;
        private const int ModPreviewMaxBytes = 64 * 1024;

        /// <summary>WPF ShareModToCatalogueAsync (ModCatalogue.cs:24): signed-in guard, the
        /// AssetSubmitDialog download-URL branch, submit, record, mod success toast.</summary>
        internal async Task ShareModToCatalogueAsync(ModPackage mod, global::Avalonia.Controls.Window? owner = null)
        {
            if (mod == null || mod.IsBuiltIn || string.IsNullOrEmpty(mod.InstalledPath)) return;
            if (!CatalogueSignedIn()) return;

            var dialog = new AssetSubmitDialog(mod.Name, CoreAccount.DisplayName,
                requireDownloadUrl: true,
                defaultTags: mod.Manifest.Tags != null ? string.Join(", ", mod.Manifest.Tags) : null);
            if (!await dialog.ShowDialogSafe<bool>(owner ?? this) || !dialog.Confirmed) return;

            JObject asset;
            try { asset = BuildModCatalogueAsset(mod, dialog.DownloadUrl); }
            catch (Exception ex)
            {
                Log.Warning(ex, "[Catalogue] Mod asset build failed");
                App.Notifications.Show(Loc.Get("catalogue_toast_unknown_error"), NotificationType.Error, TimeSpan.FromSeconds(8));
                return;
            }

            SubmissionResult result;
            try
            {
                result = await App.Catalogue.SubmitCatalogueAssetAsync(CatalogueKindMods, asset, CatalogueSchemaMod,
                    dialog.Creator, dialog.Tags, default).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[Catalogue] Mod share threw unexpectedly");
                result = new SubmissionResult.UnknownError(0, ex.Message);
            }

            RecordCatalogueSubmission(CatalogueKindMods, mod.Id, result);

            // Mods get a dedicated success toast that says where the mod ends up (the web
            // catalogue's Mods tab) and reminds the creator the .ccpmod stays on THEIR MEGA.
            if (result is SubmissionResult.Success)
                App.Notifications.Show(Loc.Get("catalogue_toast_mod_success"), NotificationType.Success, TimeSpan.FromSeconds(10));
            else
                ShowCatalogueSubmissionResultToast(result);
        }

        // ccp-mod/v1: metadata from mod.json + the creator's download link. No sha256 in v1 - the
        // installed mod is an extracted folder, so a re-zipped hash wouldn't match the MEGA bytes.
        internal static JObject BuildModCatalogueAsset(ModPackage mod, string downloadUrl)
        {
            var m = mod.Manifest;
            var asset = new JObject
            {
                ["mod_id"] = m.Id,
                ["name"] = m.Name,
                ["version"] = m.Version,
                ["author"] = m.Author,
                ["download"] = new JObject
                {
                    ["provider"] = "mega",
                    ["url"] = downloadUrl,
                    ["installed_size_bytes"] = SafeDirectorySize(mod.InstalledPath!),
                },
            };
            if (!string.IsNullOrWhiteSpace(m.Description)) asset["description"] = m.Description;
            if (!string.IsNullOrWhiteSpace(m.MinAppVersion)) asset["min_app_version"] = m.MinAppVersion;

            var thumb = TryBuildPreviewThumb(mod);
            if (thumb != null) asset["preview_thumb_b64"] = thumb;

            return asset;
        }

        private static long SafeDirectorySize(string dir)
        {
            try
            {
                return new DirectoryInfo(dir)
                    .EnumerateFiles("*", SearchOption.AllDirectories)
                    .Sum(f => f.Length);
            }
            catch { return 0; }
        }

        // Downscaled JPEG of the mod's previewImage (if it ships one), base64.
        // Returns null when there's no usable preview or it won't fit the cap.
        private static string? TryBuildPreviewThumb(ModPackage mod)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(mod.Manifest.PreviewImage) || mod.InstalledPath == null)
                    return null;
                var path = Path.Combine(mod.InstalledPath, "resources", mod.Manifest.PreviewImage);
                if (!File.Exists(path)) return null;

                // Decode near the target width (WPF DecodePixelWidth); the codec rounds to a size it can do cheaply.
                using var codec = SKCodec.Create(path);
                if (codec == null || codec.Info.Width <= 0) return null;
                var dims = codec.GetScaledDimensions(Math.Min(1f, (float)ModPreviewMaxPixels / codec.Info.Width));
                using var src = SKBitmap.Decode(codec, codec.Info.WithSize(dims.Width, dims.Height));
                if (src == null || src.Width <= 0) return null;
                var height = Math.Max(1, (int)Math.Round(src.Height * (double)ModPreviewMaxPixels / src.Width));
                using var thumb = src.Resize(new SKImageInfo(ModPreviewMaxPixels, height), new SKSamplingOptions(SKCubicResampler.Mitchell));
                if (thumb == null) return null;
                using var data = thumb.Encode(SKEncodedImageFormat.Jpeg, 80);
                if (data == null || data.Size > ModPreviewMaxBytes) return null;
                return Convert.ToBase64String(data.ToArray());
            }
            catch (Exception ex)
            {
                Log.Debug("[Catalogue] Mod preview thumb failed: {Error}", ex.Message);
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
