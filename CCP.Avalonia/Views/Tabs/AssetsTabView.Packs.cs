using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Views.Tabs;

// Adapted from parity/trunk b1423f1eab655953a434b180d00f25537069cb42 to the existing browser.
public partial class AssetsTabView
{
    // WPF MainWindow.Assets.cs:751-760: decrypted pack thumbnails are cached (LRU, 50 entries) so reopening a pack folder
    // does not decrypt every file again. UI thread only. Evicted bitmaps are left to the GC: one may still be on screen.
    // ponytail: WPF also caps the cache at 50 MB; at 100 px decode width 50 entries stay far below that.
    private const int MaxPackThumbs = 50;
    private static readonly Dictionary<string, (Bitmap Bmp, long Used)> PackThumbs = new();
    private static long _packThumbClock;

    private static string PackThumbKey(AssetThumbnailViewModel item) => $"{item.PackId}:{item.PackFile!.ObfuscatedName}";

    private static bool TryCachedPackThumb(AssetThumbnailViewModel item)
    {
        if (item.PackFile == null || !PackThumbs.TryGetValue(PackThumbKey(item), out var hit)) return false;
        PackThumbs[PackThumbKey(item)] = (hit.Bmp, ++_packThumbClock);
        item.Thumbnail = hit.Bmp;
        return true;
    }

    private static void CachePackThumb(AssetThumbnailViewModel item, Bitmap bmp)
    {
        while (PackThumbs.Count >= MaxPackThumbs)
            PackThumbs.Remove(PackThumbs.MinBy(p => p.Value.Used).Key);
        PackThumbs[PackThumbKey(item)] = (bmp, ++_packThumbClock);
    }

    private static bool IsPackLeaf(AssetTreeItem folder) =>
        folder.IsPackFolder && !string.IsNullOrEmpty(folder.PackId) && !string.IsNullOrEmpty(folder.PackFileType);

    private void AddContentPacksNode()
    {
        var store = ContentPackStore.Current;
        if (store == null) return;
        var root = new AssetTreeItem { Name = "Content Packs", IsPackFolder = true, IsExpanded = true };
        foreach (var id in store.GetActivePackIds())
        {
            var node = AssetPackTree.Build(id, store.GetPackName(id), store.GetPackFiles(id), S.DisabledAssetPaths);
            if (node == null) continue;
            node.Parent = root;
            root.Children.Add(node);
        }
        if (root.Children.Count == 0) return;
        root.FileCount = root.Children.Sum(n => n.FileCount);
        root.CheckedFileCount = root.Children.Sum(n => n.GetTotalCheckedFileCount());
        root.IsChecked = root.CheckedFileCount > 0;
        _assetTree.Add(root);
    }

    private bool TrySelectPackFolder(AssetTreeItem folder)
    {
        if (!IsPackLeaf(folder)) return false;
        _currentFolderFiles.Clear();
        var generation = ++_thumbGeneration;
        var store = ContentPackStore.Current;
        var files = store?.GetPackFiles(folder.PackId!, folder.PackFileType) ?? new List<PackFileEntry>();
        ShowEmpty(files.Count == 0 ? "label_no_files_in_this_pack_folder" : null);
        var loads = new List<Task>();
        if (store != null)
            foreach (var file in files.OrderBy(f => f.OriginalName))
            {
                var item = new AssetThumbnailViewModel(store, folder.PackId!, file)
                    { IsChecked = !S.DisabledAssetPaths.Contains(ContentPackStore.SelectionKey(folder.PackId!, file)) };
                _currentFolderFiles.Add(item);
                // ponytail: videos keep the existing placeholder; WPF's Windows shell thumbnailer needs a portable replacement.
                if (!item.IsVideo) loads.Add(LoadThumbnailAsync(item, generation));
            }
        ThumbnailLoads = Task.WhenAll(loads);
        return true;
    }
}
