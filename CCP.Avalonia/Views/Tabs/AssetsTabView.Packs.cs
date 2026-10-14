using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Views.Tabs;

// Adapted from parity/trunk b1423f1eab655953a434b180d00f25537069cb42 to the existing browser.
public partial class AssetsTabView
{
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
