using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.Content;

/// <summary>WPF MainWindow.Assets.cs BuildPackTree: virtual image/video folders and blacklist counts.</summary>
public static class PackAssetTree
{
    public static void SetSelection(string packId, IEnumerable<string> names, ISet<string> disabled, bool enabled)
    {
        foreach (var name in names)
        {
            var key = ContentPackStore.SelectionKey(packId, name);
            if (enabled) disabled.Remove(key);
            else disabled.Add(key);
        }
    }

    public static AssetTreeItem? Build(string packId, string name,
        IEnumerable<(string Name, string Type)> files, ISet<string> disabled)
    {
        var entries = files.ToList();
        if (entries.Count == 0) return null;
        var pack = new AssetTreeItem { Name = name, IsPackFolder = true, PackId = packId, FileCount = entries.Count };
        foreach (var (type, folder) in new[] { ("image", "images"), ("video", "videos") })
        {
            var matching = entries.Where(f => f.Type == type).ToList();
            if (matching.Count == 0) continue;
            var count = matching.Count(f => !disabled.Contains(ContentPackStore.SelectionKey(packId, f.Name)));
            pack.Children.Add(new AssetTreeItem
            {
                Name = folder, IsPackFolder = true, PackId = packId, PackFileType = type,
                Parent = pack, FileCount = matching.Count, CheckedFileCount = count, IsChecked = count > 0
            });
        }
        pack.IsChecked = pack.Children.Any(c => c.IsChecked);
        return pack;
    }
}
