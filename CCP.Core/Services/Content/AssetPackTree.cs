using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services;

/// <summary>Virtual asset folders from WPF MainWindow.Assets.cs:552. Both heads use the
/// same image/video order, selection keys and active-count rules.</summary>
public static class AssetPackTree
{
    public static AssetTreeItem? Build(string packId, string name, IReadOnlyList<PackFileEntry> files,
        ISet<string> disabled)
    {
        if (files.Count == 0) return null;
        var pack = new AssetTreeItem { Name = name, IsPackFolder = true, PackId = packId };
        foreach (var type in new[] { "image", "video" })
        {
            var typed = files.Where(f => f.FileType == type).ToList();
            if (typed.Count == 0) continue;
            var node = new AssetTreeItem
            {
                Name = type == "image" ? "images" : "videos", IsPackFolder = true,
                PackId = packId, PackFileType = type, Parent = pack, FileCount = typed.Count,
                CheckedFileCount = typed.Count(f => !disabled.Contains(ContentPackStore.SelectionKey(packId, f)))
            };
            node.UpdateCheckState();
            pack.Children.Add(node);
        }
        // Parent counts are display totals, not additional files to count recursively.
        pack.FileCount = files.Count;
        pack.IsChecked = pack.Children.Any(c => c.IsChecked);
        return pack;
    }
}
