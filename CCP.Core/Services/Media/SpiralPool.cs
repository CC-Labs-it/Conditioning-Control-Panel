using System;
using System.IO;
using System.Linq;
using Serilog;

namespace ConditioningControlPanel.Services;

/// <summary>WPF #641/#1053: random image pool, refusing bare personal folders and avoiding repeats.</summary>
public sealed class SpiralPool
{
    private static readonly string[] Extensions = { ".gif", ".png", ".jpg", ".jpeg", ".webp" };
    private readonly Random _random;
    private string? _previous;

    public SpiralPool(Random? random = null) => _random = random ?? new Random();

    public string? Pick(string? configured, string library)
    {
        try
        {
            var directory = string.IsNullOrEmpty(configured) ? library : Path.GetDirectoryName(configured);
            if (PersonalFolderRoots.IsPersonalFolderRoot(directory))
            {
                Log.Warning("[Overlay] Spiral randomize: refusing {Pool} as a spiral pool (personal/system folder) " +
                            "- falling back to the Spirals library", directory);
                directory = library;
            }
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return null;
            var pool = Directory.GetFiles(directory)
                .Where(f => Extensions.Contains(Path.GetExtension(f).ToLowerInvariant())).ToArray();
            if (pool.Length == 0) return null;
            if (pool.Length == 1) return pool[0];
            var candidates = pool.Where(f => f != _previous).ToArray();
            return _previous = candidates[_random.Next(candidates.Length)];
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[Overlay] Failed to pick random spiral");
            return null;
        }
    }
}
