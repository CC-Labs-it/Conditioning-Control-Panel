using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Localization;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    // WPF MainWindow.xaml.cs:4015-4110: legacy profiles that missed the wizard's folder choice.
    private static MainShellWindow? _assetsPromptOwner;
    internal static bool FirstRunAssetsPromptPending => _assetsPromptOwner != null;
    internal Func<TimeSpan, Task> FirstRunAssetsDelay = Task.Delay;
    internal Func<MainShellWindow, Task<bool>> AskFirstRunAssets = shell =>
        MessageDialog.ConfirmAsync(shell, Loc.Get("first_run_assets_title"), Loc.Get("first_run_assets_body"),
            defaultToCancel: true, okText: Loc.Get("btn_yes"), cancelText: Loc.Get("btn_no"));
    internal Func<MainShellWindow, Task<string?>> PickFirstRunAssets = PickAssetsFolder;
    internal Action<string, Window?> OfferFirstRunAssets = RemoteMediaOffer.Offer;

    private static string DefaultAssetsRoot => (global::Avalonia.Application.Current as App)?.DefaultAssetsRoot
        ?? Path.Combine(CorePaths.UserData, "assets");

    private bool NeedsFirstRunAssetsPrompt()
    {
        var s = CoreSettings.Current;
        if (CoreSettings.Service == null || !s.Welcomed || s.FirstRunAssetsPromptShown ||
            !string.IsNullOrWhiteSpace(s.CustomAssetsPath)) return false;
        static int Count(string folder, string[] extensions) => Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder).Count(f => extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            : 0;
        return Count(Path.Combine(DefaultAssetsRoot, "images"), new[] { ".png", ".jpg", ".jpeg", ".gif", ".webp" }) <= 5
            && Count(Path.Combine(DefaultAssetsRoot, "videos"), new[] { ".mp4", ".webm", ".mkv" }) <= 2;
    }

    private void ArmFirstRunAssetsPrompt()
    {
        try
        {
            if (!NeedsFirstRunAssetsPrompt()) return;
            _assetsPromptOwner = this;
            Closed += (_, _) => ReleaseFirstRunAssetsPrompt();
        }
        catch (Exception ex) { Log.Warning(ex, "Error checking first-run assets"); }
    }

    private void ReleaseFirstRunAssetsPrompt()
    {
        if (_assetsPromptOwner == this) _assetsPromptOwner = null;
    }

    private async Task CheckFirstRunAssetsPromptAsync()
    {
        if (_assetsPromptOwner != this) return;
        try
        {
            await FirstRunAssetsDelay(TimeSpan.FromMilliseconds(500));
            while (IsVisible && (!CoreSettings.Current.HasAcceptedAgeVerification || IsAgeGatePending ||
                   IsStartupDialogShowing || AppUpdater.IsUpdateDialogActive || StartupLadder.ModalPending))
                await FirstRunAssetsDelay(TimeSpan.FromMilliseconds(100));
            if (!IsVisible || !NeedsFirstRunAssetsPrompt()) return;
            IsStartupDialogShowing = true;
            try
            {
                var yes = await AskFirstRunAssets(this);
                CoreSettings.Current.FirstRunAssetsPromptShown = true;
                CoreSettings.Save();
                if (yes) await PickFirstRunAssets(this);
            }
            finally { IsStartupDialogShowing = false; }
            if (!HasAnyLocalMedia(CorePaths.EffectiveAssets))
                OfferFirstRunAssets("first-run-assets-prompt", this);
        }
        catch (Exception ex) { Log.Warning(ex, "Error in first-run assets prompt"); }
        finally { ReleaseFirstRunAssetsPrompt(); }
    }

    private static bool HasAnyLocalMedia(string root)
    {
        try
        {
            return !string.IsNullOrWhiteSpace(root) && new[] { "images", "videos" }.Any(sub =>
                Directory.Exists(Path.Combine(root, sub)) && Directory.EnumerateFiles(Path.Combine(root, sub)).Any());
        }
        catch (Exception ex) { Log.Debug(ex, "Could not check local media"); return false; }
    }
}
