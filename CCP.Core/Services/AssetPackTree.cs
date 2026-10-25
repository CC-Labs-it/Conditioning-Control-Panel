using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services;

/// <summary>WPF BuildPackTree: virtual image/video leaves and pack selection keys shared by both heads.</summary>
public static class AssetPackTree
{
    public static AssetTreeItem? Build(string packId, string name, IReadOnlyList<PackFileEntry> files, ISet<string> disabled)
    {
        if (files.Count == 0) return null;
        var node = new AssetTreeItem { Name = name, IsPackFolder = true, PackId = packId };
        foreach (var (type, label) in new[] { (ContentPackStore.ImageType, "images"), (ContentPackStore.VideoType, "videos") })
        {
            var typed = files.Where(f => f.FileType == type).ToList();
            if (typed.Count == 0) continue;
            var count = typed.Count(f => !disabled.Contains(ContentPackStore.SelectionKey(packId, f)));
            node.Children.Add(new AssetTreeItem
            {
                Name = label, IsPackFolder = true, PackId = packId, PackFileType = type,
                FileCount = typed.Count, CheckedFileCount = count, IsChecked = count > 0, Parent = node
            });
        }
        node.FileCount = files.Count;
        node.IsChecked = node.Children.Any(c => c.IsChecked);
        return node;
    }
}
