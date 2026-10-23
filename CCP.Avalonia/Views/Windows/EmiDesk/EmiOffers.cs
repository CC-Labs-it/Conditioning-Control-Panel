using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using Serilog;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.EmiDesk;

namespace ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk
{
    /// <summary>
    /// The effects an offer chip fires on this head: WPF ConditioningControlPanel/Services/EmiDesk/EmiOffers.cs.
    /// Feasibility is checked at DRAW time and fails silently (LINES-SCHEMA 4): an effect this head
    /// cannot run refuses here, so the engine never puts that ask on the glass. No dead chips.
    /// <para>ponytail: spiral, video, rain and tour: still refuse until their effect adapters are ported.</para>
    /// </summary>
    internal static class EmiOffers
    {
        private static readonly string[] ImageExts = { ".gif", ".png", ".jpg", ".jpeg", ".webp", ".bmp" };
        private static readonly string[] VideoExts = { ".mp4", ".webm", ".mkv", ".avi", ".mov", ".wmv", ".m4v" };

        /// <summary>WPF EffectFeasible: can this effect happen right now. Unknown refuses.</summary>
        public static bool EffectFeasible(string? effect)
        {
            if (string.IsNullOrWhiteSpace(effect)) return false;
            try
            {
                var e = effect.Trim();
                if (e.StartsWith("open:", StringComparison.OrdinalIgnoreCase))
                    return EmiTargets.Find(e.Substring(5)) is { Available: true, Locked: false };
                if (e.StartsWith("pinTop:", StringComparison.OrdinalIgnoreCase))
                    return EmiTargets.Find(e.Substring(7)) is { Available: true, Locked: false }
                        && !EmiState.Current.Pins.Contains(e.Substring(7), StringComparer.OrdinalIgnoreCase);
                if (e.StartsWith("book:", StringComparison.OrdinalIgnoreCase))
                    return string.Equals(e.Substring(5).Trim(), "open", StringComparison.OrdinalIgnoreCase)
                        && EmiBookCards.All.Count > 0 && EmiDeskService.Instance.Window is { Book: null };
                return e.ToLowerInvariant() switch
                {
                    "none" or "packs" => true,
                    "bedtime" => !EmiLineEngine.BedtimeSet,
                    "assets" => string.Equals(CoreSettings.Current.MediaSource, "local", StringComparison.OrdinalIgnoreCase)
                        && Shell != null,
                    "burst" => X11Overlay.IsAvailable && HasImages(),
                    "shrink" => EmiDeskService.Instance.Window is { } w && w.BodyWidth > EmiDeskWindow.MinBodyWidth + 1,
                    _ => false
                };
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] effect feasibility probe failed for {Effect}", effect);
                return false;
            }
        }

        /// <summary>WPF Run: best-effort and logged; a failed effect never takes the widget down.</summary>
        public static void Run(string? effect, bool fromAsk)
        {
            if (string.IsNullOrWhiteSpace(effect)) return;
            var e = effect.Trim();
            try
            {
                if (e.StartsWith("open:", StringComparison.OrdinalIgnoreCase))
                {
                    // WPF EmiDeskService.OpenTarget :1426.
                    var t = EmiTargets.Find(e.Substring(5));
                    if (t == null) { Log.Information("[EmiDesk] OpenTarget({Target}) ignored: not in the catalogue", e); return; }
                    Log.Information("[EmiDesk] offer opened {Target}", t.Id);
                    t.Open();
                    return;
                }
                if (e.StartsWith("pinTop:", StringComparison.OrdinalIgnoreCase))
                {
                    var id = e.Substring(7);
                    if (EmiTargets.Find(id) == null) return;
                    EmiSuggester.PinToTop(id);
                    if (EmiDeskService.Instance.Window is { RingOpen: true } ring) ring.RebuildRing();
                    EmiDeskService.Instance.Fire("pinAdded", new { target = id });
                    return;
                }
                if (e.StartsWith("book:", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.Equals(e.Substring(5).Trim(), "open", StringComparison.OrdinalIgnoreCase)) return;
                    EmiDeskService.Instance.Window?.OpenBook();
                    EmiDeskService.Instance.Fire("effectFired", new { channel = "book", fromAsk });
                    return;
                }
                switch (e.ToLowerInvariant())
                {
                    case "none": return;
                    case "bedtime":
                        EmiState.Current.BedtimeUntil = BedtimeUntil(DateTime.Now);
                        EmiState.SaveSoon();
                        EmiDeskService.Instance.Fire("bedtimeSet", new { fromAsk });
                        return;
                    case "packs": _ = AppUpdater.OpenUrl(Shell, DiscordLinks.PackCatalogue); return;
                    case "assets":
                        if (Shell is { } main) { main.ShowFromTray(); main.ShowTab("assets"); }
                        return;
                    case "burst": FireBurst(fromAsk); return;
                    case "shrink": FireShrink(); return;
                    default: Log.Debug("[EmiDesk] unknown effect {Effect}, ignored", e); return;
                }
            }
            catch (Exception ex) { Log.Warning(ex, "[EmiDesk] effect {Effect} failed", e); }
        }

        // Tests swap it: Avalonia refuses a new ApplicationLifetime once the headless app is initialized.
        internal static Func<MainShellWindow?> ShellLookup = () => MainShellWindow.Current;
        private static MainShellWindow? Shell => ShellLookup();

        // WPF SetBedtime: the next LOCAL 06:00, persisted as UTC. Exactly 06:00 means tomorrow.
        internal static DateTime BedtimeUntil(DateTime now)
        {
            var six = now.Date.AddHours(6);
            return (now >= six ? six.AddDays(1) : six).ToUniversalTime();
        }

        internal static bool LibraryIsEmpty() =>
            string.Equals(CoreSettings.Current.MediaSource, "local", StringComparison.OrdinalIgnoreCase)
                && !HasImages() && !HasVideos();

        private static bool HasVideos()
        {
            try
            {
                var dir = Path.Combine(CorePaths.EffectiveAssets, "videos");
                return Directory.Exists(dir) && Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories)
                    .Any(f => VideoExts.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase));
            }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] video scan failed"); return false; }
        }

        /// <summary>The burst a chip fires (tests swap it). WPF :494 TriggerFlashOnce(4, user duration).</summary>
        internal static Action Burst = () =>
        {
            var host = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow
                       ?? (Visual?)EmiDeskService.Instance.Window;
            if (host != null) FlashOverlay.TriggerOnce(host, amount: 4);
        };

        private static void FireBurst(bool fromAsk)
        {
            Burst();
            EmiDeskService.Instance.Fire("effectFired", new { channel = "burst", fromAsk });
        }

        /// <summary>WPF FireShrink :512: minimum width, nearest corner, saved, then "resized".</summary>
        private static void FireShrink()
        {
            var win = EmiDeskService.Instance.Window;
            if (win == null) return;
            win.ApplyBodyWidth(EmiDeskWindow.MinBodyWidth);
            win.SnapToNearestCorner();
            win.SavePlacement();
            if (Math.Abs(CoreSettings.Current.EmiDeskWidth - EmiDeskWindow.MinBodyWidth) > 0.5)
            {
                CoreSettings.Current.EmiDeskWidth = EmiDeskWindow.MinBodyWidth;
                CoreSettings.Save();
            }
            EmiDeskService.Instance.Fire("resized", new { n = (int)EmiDeskWindow.MinBodyWidth, bigger = false });
        }

        /// <summary>WPF HasImages over the user's images folder (WPF caches the walk a minute; one hit is enough here).</summary>
        internal static bool HasImages()
        {
            try
            {
                var dir = Path.Combine(CorePaths.EffectiveAssets, "images");
                return Directory.Exists(dir) && Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories)
                    .Any(f => ImageExts.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] image scan failed");
                return false;
            }
        }
    }
}
