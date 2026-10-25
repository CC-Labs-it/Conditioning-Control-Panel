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

    /// <summary>Folder under the user-data root that holds installed prompt JSON files.</summary>
    public const string FolderName = "community-prompts";

    /// <summary>WPF ImportFromFile + roster assignment: fresh ID, filename wins, then save.
    /// Invalid/missing JSON returns null, just as the reference service does.</summary>
    public static CommunityPrompt? ImportAndAssign(string path, int companionIndex)
    {
        if (companionIndex is < 0 or > 4) return null;
        try
        {
            if (!File.Exists(path)) return null;
            var prompt = JsonConvert.DeserializeObject<CommunityPrompt>(File.ReadAllText(path));
            if (prompt == null || prompt.PromptSettings == null) return null;
            prompt.Id = Guid.NewGuid().ToString("N");
            prompt.Name = Path.GetFileNameWithoutExtension(path);
            var folder = Path.Combine(CorePaths.UserData, FolderName);
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, prompt.Id + ".json"), JsonConvert.SerializeObject(prompt, Formatting.Indented));
            var settings = CoreSettings.Current;
            settings.InstalledCommunityPromptIds.Add(prompt.Id);
            settings.SetCompanionPromptId(companionIndex, prompt.Id);
            CoreSettings.Save();
            prompt.IsInstalled = true;
            if (settings.ActiveCompanionId == companionIndex) ApplyAssigned((CompanionId)companionIndex);
            return prompt;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not import roster personality");
            return null;
        }
    }

    /// <summary>WPF ApplyCompanionPrompt/ActivatePrompt. An absent assignment leaves the current
    /// voice intact; explicit-content acknowledgement is checked before any prompt is applied.</summary>
    public static bool ApplyAssigned(CompanionId companion)
    {
        var settings = CoreSettings.Current;
        var id = settings.GetCompanionPromptId((int)companion);
        if (string.IsNullOrEmpty(id)) return false;
        var prompt = Load(Path.Combine(CorePaths.UserData, FolderName), id, settings.ActiveCommunityPromptId);
        if (prompt?.PromptSettings == null) return false;
        if (Services.ExplicitContentGate.RequiresAcknowledgement(new PersonalityPreset { PromptSettings = prompt.PromptSettings }, settings.SlutModeEnabled)
            && !Services.ExplicitContentGate.IsAlreadyAcknowledged(settings.CompanionPrompt)) return false;

        var ps = prompt.PromptSettings;
        int flagged = 0;
        try
        {
            var validator = new Services.Moderation.PromptValidator();
            foreach (var (name, value) in new[] { ("Personality", ps.Personality), ("SlutModePersonality", ps.SlutModePersonality),
                ("KnowledgeBase", ps.KnowledgeBase), ("ExplicitReaction", ps.ExplicitReaction), ("ContextReactions", ps.ContextReactions) })
            {
                if (string.IsNullOrWhiteSpace(value)) continue;
                var result = validator.Validate(value);
                if (!result.Clean) { flagged++; CoreModerationLog.RecordEdit(name, result.MatchedPatterns.Count, "community_prompt"); }
            }
        }
        catch (Exception ex) { Log.Debug(ex, "Roster prompt validation failed"); }
        if (flagged > 0)
        {
            try { ValidationAdvisory?.Invoke(flagged); } catch (Exception ex) { Log.Debug(ex, "Roster prompt advisory failed"); }
        }
        settings.CompanionPrompt = ps;
        ps.UseCustomPrompt = true;
        settings.ActiveCommunityPromptId = id;
        CoreSettings.Save();
        return true;
    }

    public static CommunityPrompt? Load(string folder, string promptId, string? activePromptId)
    {
        try
        {
            // Prompt IDs are filenames, never paths supplied by an imported manifest.
            if (string.IsNullOrEmpty(promptId) || promptId.Contains('/') || promptId.Contains('\\')
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
