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
