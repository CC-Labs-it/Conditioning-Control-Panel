using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Content;
using PackService = ConditioningControlPanel.Services.Packs.ContentPackService;

namespace ConditioningControlPanel.Avalonia.Views.Tabs;

public partial class AssetsTabView
{
    // WPF keeps the download cards hidden too. This slice restores the reachable active-pack tree.
    private void AppendPackTree()
    {
        var store = ContentPackStore.Current;
        if (store == null) return;
        var root = new AssetTreeItem { Name = "Content Packs", IsPackFolder = true, IsExpanded = true };
        var builtIns = PackService.Current?.GetBuiltInPacks();
        foreach (var id in store.GetActivePackIds())
        {
            var name = builtIns?.FirstOrDefault(p => p.Id == id)?.Name ?? id;
            var pack = PackAssetTree.Build(id, name,
                store.GetPackFiles(id).Select(f => (f.OriginalName, f.FileType)), S.DisabledAssetPaths);
            if (pack == null) continue;
            pack.Parent = root;
            root.Children.Add(pack);
        }
        if (root.Children.Count == 0) return;
        root.FileCount = root.Children.Sum(p => p.FileCount);
        root.CheckedFileCount = root.Children.Sum(p => p.GetTotalCheckedFileCount());
        root.IsChecked = root.CheckedFileCount > 0;
        _assetTree.Add(root);
    }

    private static AssetTreeItem? FindPackFolder(IEnumerable<AssetTreeItem> nodes, string id, string? type)
    {
        foreach (var node in nodes)
        {
            if (node.IsPackFolder && node.PackId == id && node.PackFileType == type) return node;
            if (FindPackFolder(node.Children, id, type) is { } found) return found;
        }
        return null;
    }

    private void LoadPackThumbnails(AssetTreeItem folder)
    {
        _currentFolderFiles.Clear();
        var generation = ++_thumbGeneration;
        var files = ContentPackStore.Current?.GetPackFiles(folder.PackId!, folder.PackFileType) ?? new();
        files = files.OrderBy(f => f.OriginalName).ToList();   // WPF MainWindow.Assets.cs:724
        ShowEmpty(files.Count == 0 ? "label_no_files_in_this_pack_folder" : null);
        var loads = new List<Task>();
        foreach (var file in files)
        {
            var key = ContentPackStore.SelectionKey(folder.PackId!, file);
            var item = new AssetThumbnailViewModel(file.OriginalName, key)
            {
                PackId = folder.PackId, PackFile = file, IsChecked = !S.DisabledAssetPaths.Contains(key)
            };
            _currentFolderFiles.Add(item);
            // ponytail: videos retain the same placeholder as local videos; Windows shell decode is a follow-up.
            if (!item.IsVideo) loads.Add(LoadThumbnailAsync(item, generation));
        }
        ThumbnailLoads = Task.WhenAll(loads);
    }
}
