using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.Views.Tabs;

// Adapted from parity/trunk 7b6f94800e90a90fa443fb47efcfaeedc17eff0e.
public partial class QuestsTabView
{
    private const string PackResources = "pack://application:,,,/Resources/";
    private static readonly Dictionary<string, Bitmap> QuestArtCache = new(StringComparer.OrdinalIgnoreCase);
    private static string? _questArtModId;

    internal static void ClearQuestArtCache()
    {
        // Images already on screen retain their bitmap until their repaint; do not dispose it here.
        QuestArtCache.Clear();
        _questArtModId = CoreMods.ActiveModId;
    }

    internal static Bitmap? GetQuestArt(QuestDefinition def)
    {
        try
        {
            if (_questArtModId != CoreMods.ActiveModId) ClearQuestArtCache();
            var path = def.EffectiveImagePath;
            if (string.IsNullOrEmpty(path)) return null;
            if (path.StartsWith(PackResources, StringComparison.Ordinal))
            {
                var resource = path.Substring(PackResources.Length);
                var replacement = CoreModArt.OverridePath(resource);
                path = replacement != null && File.Exists(replacement) ? replacement
                    : PackResources + (QuestStampRules.LegacyImageOverride(resource, CoreSettings.Current.IsSissyMode, CoreMods.IsCCPDefault) ?? resource);
            }
            if (QuestArtCache.TryGetValue(path, out var cached)) return cached;
            Bitmap? image = null;
            if (path.StartsWith(PackResources, StringComparison.Ordinal))
            {
                var uri = new Uri("avares://CCP.Avalonia/Resources/" + path.Substring(PackResources.Length));
                if (!AssetLoader.Exists(uri)) return null;
                using var stream = AssetLoader.Open(uri);
                image = Bitmap.DecodeToWidth(stream, 512);
            }
            else if (File.Exists(path)) image = new Bitmap(path);
            if (image != null) QuestArtCache[path] = image;
            return image;
        }
        catch { return null; } // WPF's missing-art path is the Fluent quest glyph.
    }
}
