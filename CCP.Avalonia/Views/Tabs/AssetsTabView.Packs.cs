using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Packs;

namespace ConditioningControlPanel.Avalonia.Views.Tabs;

public partial class AssetsTabView
{
    // WPF MainWindow.Assets.cs:517-623. Keep virtual folders out of disk-folder exclusions.
    private static string FolderKey(AssetTreeItem folder) => folder.IsPackFolder
        ? $"pack:{folder.PackId}/{folder.PackFileType}" : folder.FullPath;

    private static List<PackFileEntry>? PackFiles(AssetTreeItem folder) =>
        folder.IsPackFolder && !string.IsNullOrEmpty(folder.PackId) && !string.IsNullOrEmpty(folder.PackFileType)
            ? ContentPackStore.Current?.GetPackFiles(folder.PackId, folder.PackFileType) : null;

    private void AddPackTree()
    {
        var store = ContentPackStore.Current;
        if (store == null) return;
        var root = new AssetTreeItem { Name = "Content Packs", IsPackFolder = true, IsExpanded = true };
        foreach (var id in store.GetActivePackIds())
        {
            var files = store.GetPackFiles(id);
            if (files.Count == 0) continue;
            var pack = new AssetTreeItem
            {
                Name = ContentPackService.Current?.GetBuiltInPacks().FirstOrDefault(p => p.Id == id)?.Name ?? id,
                IsPackFolder = true, PackId = id, FileCount = files.Count, Parent = root
            };
            foreach (var type in new[] { "image", "video" })
            {
                var typed = files.Where(f => f.FileType == type).ToList();
                if (typed.Count == 0) continue;
                var child = new AssetTreeItem
                {
                    Name = type == "image" ? "images" : "videos", IsPackFolder = true,
                    PackId = id, PackFileType = type, FileCount = typed.Count,
                    CheckedFileCount = typed.Count(f => !S.DisabledAssetPaths.Contains(ContentPackStore.SelectionKey(id, f))),
                    Parent = pack
                };
                child.UpdateCheckState();
                pack.Children.Add(child);
            }
            pack.UpdateCheckState();
            root.Children.Add(pack);
        }
        if (root.Children.Count == 0) return;
        root.FileCount = root.Children.Sum(p => p.FileCount);
        root.UpdateCheckState();
        _assetTree.Add(root);
    }

    private void LoadPackFolderThumbnails(AssetTreeItem folder, List<PackFileEntry> files)
    {
        _currentFolderFiles.Clear();
        var generation = ++_thumbGeneration;
        ShowEmpty(files.Count == 0 ? "label_no_files_in_this_pack_folder" : null);
        var loads = new List<Task>();
        foreach (var file in files.OrderBy(f => f.OriginalName))
        {
            var key = ContentPackStore.SelectionKey(folder.PackId!, file);
            var item = new AssetThumbnailViewModel(file.OriginalName, key)
            {
                PackId = folder.PackId, PackFileEntry = file,
                Name = file.OriginalName, IsVideo = file.FileType == "video",
                IsChecked = !S.DisabledAssetPaths.Contains(key)
            };
            _currentFolderFiles.Add(item);
            // ponytail: videos keep the existing Fluent placeholder until a portable thumbnailer lands.
            if (!item.IsVideo) loads.Add(LoadThumbnailAsync(item, generation));
        }
        ThumbnailLoads = Task.WhenAll(loads);
    }
}
