using System;
using System.Linq;
using System.IO;
using ConditioningControlPanel;
using Serilog;
using ConditioningControlPanel.Services.EmiDesk;

namespace ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;

// WPF Services/EmiDesk/EmiOffers.cs:42,267,536. Unsupported effects stay infeasible.
internal static class EmiOffers
{
    // WPF EmiOffers.cs:346-370. Scan again at the tick so newly installed media counts.
    internal static bool LibraryIsEmpty()
    {
        try
        {
            if (!string.Equals(CoreSettings.Current?.MediaSource, "local", StringComparison.OrdinalIgnoreCase)) return false;
            bool Has(string folder, string[] extensions)
            {
                var dir = Path.Combine(CorePaths.EffectiveAssets, folder);
                return Directory.Exists(dir) && Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                    .Any(f => extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase));
            }
            return !Has("images", new[] { ".gif", ".png", ".jpg", ".jpeg", ".webp", ".bmp" })
                && !Has("videos", new[] { ".mp4", ".webm", ".mkv", ".avi", ".mov", ".wmv", ".m4v" });
        }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] empty-library probe failed"); return false; }
    }

    internal static void AnnounceEmptyLibrary(EmiDeskService? desk = null)
    {
        if (LibraryIsEmpty()) (desk ?? EmiDeskService.Instance).EmptyLibraryMoment("noMediaYet");
    }

    internal static bool EffectFeasible(string? effect)
    {
        if (string.IsNullOrWhiteSpace(effect)) return false;
        var e = effect.Trim();
        var desk = EmiDeskService.Instance;
        if (e.StartsWith("open:", StringComparison.OrdinalIgnoreCase))
            return desk.IsTargetAvailable(e.Substring(5));
        if (e.StartsWith("pinTop:", StringComparison.OrdinalIgnoreCase))
        {
            var id = e.Substring(7);
            return desk.IsTargetAvailable(id) && !EmiState.Current.Pins.Contains(id, StringComparer.OrdinalIgnoreCase);
        }
        return e.Equals("none", StringComparison.OrdinalIgnoreCase) ||
            (e.Equals("bedtime", StringComparison.OrdinalIgnoreCase) && !EmiLineEngine.BedtimeSet);
    }

    internal static void Run(string? effect, bool fromAsk)
    {
        if (string.IsNullOrWhiteSpace(effect)) return;
        var e = effect.Trim();
        var desk = EmiDeskService.Instance;
        if (e.StartsWith("open:", StringComparison.OrdinalIgnoreCase)) desk.OpenTarget(e.Substring(5));
        else if (e.StartsWith("pinTop:", StringComparison.OrdinalIgnoreCase)) desk.PinTop(e.Substring(7));
        else if (e.Equals("bedtime", StringComparison.OrdinalIgnoreCase))
        {
            var now = DateTime.Now;
            var six = now.Date.AddHours(6);
            if (now >= six) six = six.AddDays(1);
            EmiState.Current.BedtimeUntil = six.ToUniversalTime();
            EmiState.SaveSoon();
            desk.Fire("bedtimeSet", new { fromAsk });
        }
    }
}
