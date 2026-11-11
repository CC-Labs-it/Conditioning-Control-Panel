using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Services.Chaos;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;

/// <summary>WPF EmiGifRain and the local-image half of EmiOffers.</summary>
internal static class EmiGifRain
{
    private static readonly string[] Extensions = { ".gif", ".png", ".jpg", ".jpeg", ".webp", ".bmp" };
    private static int _generation;
    private static bool _pending;
    private static long _ownedRun;
    internal static Task? LastPick { get; private set; }

    internal static List<string> Images()
    {
        try
        {
            var dir = Path.Combine(CorePaths.EffectiveAssets, "images");
            return Directory.Exists(dir) ? Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories)
                .Where(f => Extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                .Take(4000).ToList() : new();
        }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] image scan failed"); return new(); }
    }

    internal static bool Start(EmiDeskWindow? owner)
    {
        if (_pending || ChaosGifCascadeOverlay.IsRaining || Images().Count == 0) return false;
        _pending = true;
        int generation = ++_generation;
        long cascadeVersion = ChaosGifCascadeOverlay.RunId;
        var settings = CoreSettings.Current;
        int count = GifCascade.BatchSize(GifCascade.PayloadSpawnRatePerSec, 10);
        LastPick = Task.Run(() => ChaosGifCascadeOverlay.PickFiles(count, settings)).ContinueWith(t =>
            Dispatcher.UIThread.Post(() =>
            {
                if (generation != _generation) return;
                _pending = false;
                try
                {
                    if (cascadeVersion != ChaosGifCascadeOverlay.RunId || ChaosGifCascadeOverlay.IsRaining ||
                        !t.IsCompletedSuccessfully || t.Result.Count == 0) return;
                    ChaosGifCascadeOverlay.ShowWithFiles(t.Result, GifCascade.PayloadSpawnRatePerSec, 10,
                        GifCascade.PayloadGifSize, GifCascade.PayloadFallSpeed, GifCascade.PayloadOpacity, GifCascade.PayloadStartScale);
                    if (!ChaosGifCascadeOverlay.IsRaining) return;
                    _ownedRun = ChaosGifCascadeOverlay.RunId;
                    if (owner?.IsVisible == true) { owner.Topmost = false; owner.Topmost = true; }
                }
                catch (Exception ex) { Log.Warning(ex, "[EmiDesk] rain failed"); }
            }), TaskScheduler.Default);
        return true;
    }

    internal static void Stop()
    {
        ++_generation;
        _pending = false;
        if (_ownedRun != 0 && _ownedRun == ChaosGifCascadeOverlay.RunId) ChaosGifCascadeOverlay.CloseActive();
        _ownedRun = 0;
    }
}
