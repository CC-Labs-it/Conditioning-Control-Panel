using System;
using System.Collections.Generic;
using System.IO;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Services.Deeper;

/// <summary>WPF MainWindow.DeeperTab.ImportEnhancementFiles, without presentation.</summary>
public sealed class EnhancementImportBatch
{
    public List<string> Imported { get; } = new();
    public List<string> Errors { get; } = new();
    public int Skipped { get; private set; }
    public string? LastImportedPath { get; private set; }
    public string? DuplicatePath { get; private set; }

    public static EnhancementImportBatch Run(EnhancementLibrary library, IEnumerable<string> paths)
    {
        var result = new EnhancementImportBatch();
        string? first = null;
        foreach (var path in paths)
        {
            first ??= path;
            var name = Path.GetFileName(path);
            if (!EnhancementImportRules.IsImportablePath(path))
            {
                result.Errors.Add(string.Format(Loc.Get("deeper_import_err_not_enh_fmt"), name));
                continue;
            }
            try
            {
                if (!EnhancementImportRules.FileLooksLikeEnhancement(path)) { result.Skipped++; continue; }
                if (library.FindDuplicateOf(path) is { } duplicate) { result.DuplicatePath = duplicate; continue; }
                var enhancement = EnhancementSerializer.LoadFromFile(path);
                var saved = library.PromoteToLibrary(enhancement, "import");
                if (saved == null) result.Errors.Add(string.Format(Loc.Get("deeper_import_err_write_fmt"), name));
                else
                {
                    result.LastImportedPath = saved;
                    result.Imported.Add(Path.GetFileName(saved));
                }
            }
            catch (EnhancementLoadException ex) { result.Errors.Add($"{name}: {ex.Message}"); }
            catch (Exception ex) { result.Errors.Add($"{name}: {ex.GetType().Name}"); }
        }
        if (result.LastImportedPath != null && !string.IsNullOrEmpty(first))
        {
            var directory = Path.GetDirectoryName(first);
            if (!string.IsNullOrEmpty(directory))
            {
                CoreSettings.Current.DeeperLastDirectory = directory;
                CoreSettings.Save();
            }
        }
        return result;
    }
}
