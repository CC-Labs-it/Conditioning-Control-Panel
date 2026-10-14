using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Views.Tabs;

public partial class AssetsTabView
{
    private void AddPackTree()
    {
        var store = ContentPackStore.Current;
        if (store == null) return;
        var root = new AssetTreeItem { Name = "Content Packs", IsPackFolder = true, IsExpanded = true };
        foreach (var id in store.GetActivePackIds())
        {
            var name = ConditioningControlPanel.Services.Packs.ContentPackService.Current?.GetBuiltInPacks()
                .FirstOrDefault(p => p.Id == id)?.Name ?? store.GetPackName(id);
            var pack = AssetPackTree.Build(id, name, store.GetPackFiles(id), S.DisabledAssetPaths);
            if (pack == null) continue;
            pack.Parent = root;
            root.Children.Add(pack);
        }
        if (root.Children.Count == 0) return;
        root.FileCount = root.Children.Sum(p => p.FileCount);
        root.IsChecked = root.Children.Any(p => p.IsChecked);
        _assetTree.Add(root);
    }

    private static AssetTreeItem? FindPack(IEnumerable<AssetTreeItem> nodes, string id, string? type)
    {
        foreach (var node in nodes)
        {
            if (node.PackId == id && node.PackFileType == type) return node;
            if (FindPack(node.Children, id, type) is { } match) return match;
        }
        return null;
    }

    private void LoadPackFolderThumbnails(string id, string type)
    {
        _currentFolderFiles.Clear();
        var generation = ++_thumbGeneration;
        var loads = new List<Task>();
        var files = ContentPackStore.Current?.GetPackFiles(id, type) ?? new();
        ShowEmpty(files.Count == 0 ? "label_no_files_in_this_pack_folder" : null);
        foreach (var file in files.OrderBy(f => f.OriginalName))
        {
            var key = ContentPackStore.SelectionKey(id, file);
            var item = new AssetThumbnailViewModel(file.OriginalName, key)
                { PackId = id, PackFile = file, IsChecked = !S.DisabledAssetPaths.Contains(key) };
            _currentFolderFiles.Add(item);
            // ponytail: video thumbnails still need the platform thumbnail seam; images decrypt in memory.
            if (!item.IsVideo) loads.Add(LoadThumbnailAsync(item, generation));
        }
        ThumbnailLoads = Task.WhenAll(loads);
    }
}
