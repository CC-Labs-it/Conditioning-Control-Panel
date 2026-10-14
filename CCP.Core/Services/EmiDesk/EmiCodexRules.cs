using System;
using System.IO;
using System.Linq;
using Serilog;

namespace ConditioningControlPanel.Services.EmiDesk;

/// <summary>Content and offer brakes from WPF EmiCodex. The heads keep window ownership,
/// scheduling and the moment bus; these checks never construct a browser or write a profile.</summary>
public static class EmiCodexRules
{
    public static bool BundlePresent(string? bundleRoot)
    {
        try
        {
            return !string.IsNullOrEmpty(bundleRoot)
                && File.Exists(Path.Combine(bundleRoot, "index.html"));
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiCodex] bundle probe failed");
            return false;
        }
    }

    /// <summary>WPF counts chapter files, even malformed ones: the reader independently skips
    /// unreadable chapters. A directory alone does not promise a readable book.</summary>
    public static bool HasContent(string? bundleRoot)
    {
        try
        {
            if (BundlePresent(bundleRoot)) return true;
            if (string.IsNullOrEmpty(bundleRoot)) return false;
            var chapters = Path.Combine(bundleRoot, "chapters");
            return Directory.Exists(chapters) && Directory.EnumerateFiles(chapters, "*.json").Any();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiCodex] content probe failed");
            return false;
        }
    }

    /// <summary>The three brakes before firing bookOffer. The engine still owns the ever/1
    /// budget and ask cadence; passing here does not bypass either. The count stays lazy so
    /// absent content or an open window refuses before reading the persisted profile.</summary>
    public static bool CanOffer(bool hasContent, bool isOpen, Func<int> codexOpens)
        => hasContent && !isOpen && codexOpens() <= 0;
}
