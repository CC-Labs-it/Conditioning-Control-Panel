using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Packs;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Tabs;

public partial class AssetsTabView
{
    // WPF MainWindow.Assets.cs:50-105,375-412,1979-2130. Keep the catalogue hidden as WPF does.
    // All pack persistence, download/authentication and selection state use the existing Core services.
    private bool _refreshingPacks;
    internal Func<string, Task> OpenPackLink = null!;

    private void InitializePackCatalogue()
    {
        OpenPackLink = url => Platform.ExternalOpener.OpenAsync(TopLevel.GetTopLevel(this), url);
        BtnRefreshPacks.Click += async (_, _) => await RefreshPacksAsync();
        BtnDeleteDownloadedPacks.Click += async (_, _) => await DeleteDownloadedPacksAsync();
        BtnCreatorDiscord.Click += async (_, _) => await OpenPackLink(DiscordLinks.Invite);
        PacksScrollViewer.AddHandler(PointerWheelChangedEvent, PacksScrollViewer_Wheel,
            RoutingStrategies.Tunnel);
    }

    internal async Task RefreshPacksAsync()
    {
        if (_refreshingPacks) return;
        _refreshingPacks = true;
        try
        {
            if (ContentPackService.Current is not { } service) return;
            var packs = await service.GetAvailablePacksAsync();
            var cards = ((AssetsTabViewModel)DataContext!).Packs;
            cards.Clear();
            foreach (var pack in packs) cards.Add(new PackCardViewModel(pack));
            PacksScrollViewer.InvalidateMeasure();
        }
        catch (Exception ex) { Log.Warning(ex, "Failed to refresh packs"); }
        finally { _refreshingPacks = false; }
    }

    private void PacksScrollViewer_Wheel(object? sender, PointerWheelEventArgs e)
    {
        // One WPF wheel notch has Delta=120; Avalonia expresses it as one unit.
        PacksScrollViewer.Offset = new Vector(PacksScrollViewer.Offset.X - e.Delta.Y * 120,
            PacksScrollViewer.Offset.Y);
        e.Handled = true;
    }

    private void RefreshPackAssets()
    {
        RefreshAssetTree();
        InvalidateAssetPoolsAfterSelectionChange();
        if (DataContext is not AssetsTabViewModel vm || ContentPackStore.Current is not { } store) return;
        foreach (var card in vm.Packs)
        {
            card.Pack.IsDownloaded = store.IsPackInstalled(card.Id);
            card.Pack.IsActive = store.IsPackActive(card.Id);
        }
    }

    internal async Task DeleteDownloadedPacksAsync()
    {
        var ids = S.InstalledPackIds?.ToList();
        if (ids == null || ids.Count == 0)
        {
            await Inform(Loc.Get("title_delete_downloaded_packs"), Loc.Get("msg_no_downloaded_packs_to_delete"));
            return;
        }
        if (!await Confirm(Loc.Get("title_delete_downloaded_packs"),
                Loc.GetF("msg_delete_downloaded_packs_confirm_0", ids.Count))) return;
        try
        {
            var store = ContentPackStore.Current ?? throw new InvalidOperationException("Content packs unavailable.");
            foreach (var id in ids) store.UninstallPack(id);
            RefreshPackAssets();
            if (ids.Any(store.IsPackInstalled)) throw new InvalidOperationException("Some downloaded packs could not be deleted.");
            await Inform(Loc.Get("btn_done"), Loc.Get("msg_all_downloaded_packs_have_been_deleted_nyour"));
        }
        catch (Exception ex) { await Inform("Error", ex.Message); }
    }

    private async void BtnPackDownload_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: PackCardViewModel card }) await ChangePackDownloadAsync(card);
    }

    internal async Task ChangePackDownloadAsync(PackCardViewModel card)
    {
        if (card.IsDownloading) return;
        var pack = card.Pack;
        var service = ContentPackService.Current;
        string? authenticationMessage = null;
        void OnAuthentication(object? sender, string message) => authenticationMessage = message;
        if (service != null) service.AuthenticationRequired += OnAuthentication;
        try
        {
            if (pack.IsDownloaded)
            {
                var size = pack.SizeBytes > 0 ? $"{pack.SizeBytes / (1024.0 * 1024 * 1024):F1} GB" : "";
                if (!await Confirm("Uninstall Content Pack", $"Uninstall '{pack.Name}'?\n\nThis will delete {size} of downloaded content from your computer.\n\nYou can reinstall it later if needed.")) return;
                var store = ContentPackStore.Current ?? throw new InvalidOperationException("Content packs unavailable.");
                store.UninstallPack(pack.Id);
                RefreshPackAssets();
                if (store.IsPackInstalled(pack.Id)) throw new InvalidOperationException("Downloaded content could not be deleted.");
                await Inform("Uninstalled", $"'{pack.Name}' has been uninstalled.");
                return;
            }
            if (pack.IsExternal)
            {
                var url = pack.ExternalUrl ?? (service == null ? null : await service.GetExternalPackDownloadUrlAsync(pack.Id));
                if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
                    await OpenPackLink(url!);
                if (authenticationMessage != null) await ShowPackAuthenticationAsync(authenticationMessage);
                return;
            }
            var downloadSize = pack.SizeBytes > 0 ? $" ({pack.SizeBytes / (1024.0 * 1024):F0} MB)" : "";
            if (!await Confirm("Install Content Pack", $"Download and install '{pack.Name}'?{downloadSize}\n\nThis will download encrypted content to a secure folder on your computer.")) return;
            if (service == null) throw new InvalidOperationException("Content packs unavailable.");
            pack.IsDownloading = true;
            await service.InstallPackAsync(pack, new Progress<int>(p => pack.DownloadProgress = p));
            ContentPackStore.Current!.ActivatePack(pack.Id);
            RefreshPackAssets();
            await Inform(Loc.Get("title_success"), Loc.GetF("msg_0_installed_successfully", pack.Name));
        }
        catch (UnauthorizedAccessException ex) { await ShowPackAuthenticationAsync(authenticationMessage ?? ex.Message); }
        catch (PackRateLimitException ex)
        {
            var until = ex.ResetTime - DateTime.UtcNow;
            var hours = until.TotalHours > 1 ? Loc.GetF("label_0_hours", (int)until.TotalHours)
                : Loc.GetF("label_0_minutes", (int)until.TotalMinutes);
            await Inform(Loc.Get("title_download_limit_reached"), Loc.GetF("msg_download_limit_reached_0_1", ex.Message, hours));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Pack action failed for {PackId}", pack.Id);
            await Inform("Error", ex.Message);
        }
        finally
        {
            if (service != null) service.AuthenticationRequired -= OnAuthentication;
            pack.IsDownloading = false;
            pack.DownloadProgress = 0;
        }
    }

    private Task ShowPackAuthenticationAsync(string message) => CoreAccount.IsLoggedIn
        ? Inform(Loc.Get("title_authentication_required"), message)
        : Inform(Loc.Get("title_login_required"), Loc.GetF("msg_0_n_nplease_log_in_from_the_settings_tab", message));

    private async void BtnPackActivate_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: PackCardViewModel card } || !card.IsDownloaded || card.IsDownloading) return;
        try
        {
            var store = ContentPackStore.Current ?? throw new InvalidOperationException("Content packs unavailable.");
            if (card.IsActive) store.DeactivatePack(card.Id); else store.ActivatePack(card.Id);
            RefreshPackAssets();
        }
        catch (Exception ex) { await Inform("Error", $"Failed to update pack: {ex.Message}"); }
    }
}

public sealed class AssetsTabViewModel
{
    public ObservableCollection<PackCardViewModel> Packs { get; } = new();
}

/// <summary>The head's card adapter: shared Core pack state, marshalled back to the UI dispatcher.</summary>
public sealed class PackCardViewModel : INotifyPropertyChanged
{
    internal ContentPack Pack { get; }
    public PackCardViewModel(ContentPack pack)
    {
        Pack = pack;
        pack.PropertyChanged += (_, e) =>
        {
            void Notify() => PropertyChanged?.Invoke(this, e);
            if (Dispatcher.UIThread.CheckAccess()) Notify(); else Dispatcher.UIThread.Post(Notify);
        };
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public string Id => Pack.Id;
    public string Name => Pack.Name;
    public string Description => Pack.Description;
    public string SizeDisplay => Pack.SizeDisplay;
    public string CountsDisplay => $"{Pack.ImageCount} images, {Pack.VideoCount} videos";
    public bool IsDownloaded => Pack.IsDownloaded;
    public bool IsActive => Pack.IsActive;
    public bool IsExternal => Pack.IsExternal;
    public bool IsDownloading => Pack.IsDownloading;
    public double DownloadProgress => Pack.DownloadProgress;
    public bool IsNotDownloading => Pack.IsNotDownloading;
    public bool ShowExternalButtons => Pack.ShowExternalButtons;
    public string DownloadButtonText => Pack.DownloadButtonText;
    public string ActivateButtonText => Pack.ActivateButtonText;
    // ponytail: pack preview decoding/rotation and card sheen remain in wire-views-tab-assets-c.
    public IImage? CurrentPreviewImage => null;
    public IImage? PreviewImage => null;
    public bool HasPreviewImages => false;
    public bool HasAnyPreview => false;
}
