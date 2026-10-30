using System;
using System.IO;
using ConditioningControlPanel.Models;
using Newtonsoft.Json;
using Serilog;

namespace ConditioningControlPanel.Services.Companion;

/// <summary>The installed JSON lookup used by both heads. No catalogue/network work is needed
/// to show the active prompt's name, and opening the editor does not create a prompt folder.</summary>
public static class InstalledCommunityPrompts
{
    public static event Action<int>? ValidationAdvisory;

    // WPF MainWindow.CompanionTab.cs:433-448: import, persist assignment, apply if active.
    public static CommunityPrompt? ImportAndAssign(string path, int companionIndex)
    {
        if (companionIndex is < 0 or > 4) return null;
        using var service = new CommunityPromptService();
        var prompt = service.ImportFromFile(path);
        if (prompt == null) return null;
        CoreSettings.Current.SetCompanionPromptId(companionIndex, prompt.Id);
        CoreSettings.Save();
        if (CoreSettings.Current.ActiveCompanionId == companionIndex)
            ApplyAssigned((CompanionId)companionIndex);
        return prompt;
    }

    // WPF CompanionService.cs:424-446: missing assignments keep the current voice.
    // Reuse the base service's acknowledgement gate and soft moderation pass.
    public static bool ApplyAssigned(CompanionId companion)
    {
        try
        {
            var id = CoreSettings.Current.GetCompanionPromptId((int)companion);
            if (string.IsNullOrEmpty(id)) return false;
            using var service = new CommunityPromptService();
            if (service.GetInstalledPrompt(id)?.PromptSettings == null) return false;
            service.ValidationAdvisory += (_, count) => ValidationAdvisory?.Invoke(count);
            return service.ActivatePrompt(id);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not apply assigned companion prompt");
            return false;
        }
    }

    /// <summary>Folder under the user-data root that holds installed prompt JSON files.</summary>
    public const string FolderName = "community-prompts";

    public static CommunityPrompt? Load(string folder, string promptId, string? activePromptId)
    {
        try
        {
            // Prompt IDs are filenames, never paths supplied by an imported manifest.
            if (string.IsNullOrWhiteSpace(promptId) || promptId.Contains('/') || promptId.Contains('\\')
                || promptId.Contains(':')
                || promptId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                return null;
            var path = Path.Combine(folder, promptId + ".json");
            if (!File.Exists(path)) return null;
            var prompt = JsonConvert.DeserializeObject<CommunityPrompt>(File.ReadAllText(path));
            if (prompt != null)
            {
                prompt.IsInstalled = true;
                prompt.IsActive = promptId == activePromptId;
            }
            return prompt;
        }
        catch (Exception ex)
        {
            Log.Warning("Error loading prompt {Id}: {Error}", promptId, ex.Message);
            return null;
        }
    }
}
