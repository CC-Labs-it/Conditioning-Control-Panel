using System;
using System.Collections.Generic;
using System.IO;
using ConditioningControlPanel.Localization;
using Serilog;

namespace ConditioningControlPanel.Services.Deeper;

/// <summary>WPF ImportEnhancementFiles' batch processing. Both heads own their picker,
/// notifications and row reveal; validation, duplicate detection and promotion run once here.</summary>
public sealed class EnhancementImportBatch
{
    public List<string> Imported { get; } = new();
    public List<string> Errors { get; } = new();
    public int SkippedNonEnhancement { get; private set; }
    public string? LastImportedPath { get; private set; }
    public string? DuplicateOfPath { get; private set; }
    public string? RevealPath => LastImportedPath ?? DuplicateOfPath;

    public static EnhancementImportBatch Run(EnhancementLibrary library, IEnumerable<string> paths)
    {
        var result = new EnhancementImportBatch();
        foreach (var path in paths)
        {
            var fileName = Path.GetFileName(path);
            if (!EnhancementImportRules.IsImportablePath(path))
            {
                result.Errors.Add(Loc.GetF("deeper_import_err_not_enh_fmt", fileName));
                continue;
            }
            try
            {
                if (!EnhancementImportRules.FileLooksLikeEnhancement(path))
                {
                    result.SkippedNonEnhancement++;
                    continue;
                }
                var existing = library.FindDuplicateOf(path);
                if (existing != null) { result.DuplicateOfPath = existing; continue; }
                var enhancement = EnhancementSerializer.LoadFromFile(path);
                var saved = library.PromoteToLibrary(enhancement, sourceTag: "import");
                if (saved == null)
                {
                    result.Errors.Add(Loc.GetF("deeper_import_err_write_fmt", fileName));
                    continue;
                }
                result.LastImportedPath = saved;
                result.Imported.Add(Path.GetFileName(saved));
            }
            catch (EnhancementLoadException ex) { result.Errors.Add($"{fileName}: {ex.Message}"); }
            catch (Exception ex)
            {
                Log.Warning(ex, "ImportEnhancementFiles: import failed");
                result.Errors.Add($"{fileName}: {ex.GetType().Name}");
            }
        }
        return result;
    }
}
