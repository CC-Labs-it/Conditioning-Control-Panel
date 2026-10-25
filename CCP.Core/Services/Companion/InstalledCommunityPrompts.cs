using System;
using System.IO;
using ConditioningControlPanel.Services.Moderation;
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

    // Adapted from WPF CommunityPromptService.ImportFromFile and CompanionService.
    // ponytail: catalogue download/export remains head work; local assignment needs no network host.
    public static CommunityPrompt? Import(string folder, string file, AppSettings settings)
    {
        var prompt = JsonConvert.DeserializeObject<CommunityPrompt>(File.ReadAllText(file));
        if (prompt == null || prompt.PromptSettings == null) return null;
        prompt.Id = Guid.NewGuid().ToString("N");
        prompt.Name = Path.GetFileNameWithoutExtension(file);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, prompt.Id + ".json"), JsonConvert.SerializeObject(prompt, Formatting.Indented));
        settings.InstalledCommunityPromptIds.Add(prompt.Id);
        prompt.IsInstalled = true;
        return prompt;
    }

    public static string? AssignedName(string folder, AppSettings settings, int companion)
    {
        var id = settings.GetCompanionPromptId(companion);
        return string.IsNullOrEmpty(id) ? null : Load(folder, id, settings.ActiveCommunityPromptId)?.Name;
    }

    /// <summary>WPF ApplyCompanionPrompt: no assignment leaves the current voice alone; the
    /// service refuses explicit activation without the existing acknowledgement.</summary>
    public static bool ApplyAssigned(string folder, AppSettings settings, int companion, Action<int>? advisory = null)
    {
        var id = settings.GetCompanionPromptId(companion);
        var prompt = string.IsNullOrEmpty(id) ? null : Load(folder, id, settings.ActiveCommunityPromptId);
        if (prompt?.PromptSettings == null) return false;
        if (ExplicitContentGate.RequiresAcknowledgement(new PersonalityPreset { PromptSettings = prompt.PromptSettings }, settings.SlutModeEnabled)
            && !ExplicitContentGate.IsAlreadyAcknowledged(settings.CompanionPrompt)) return false;
        int flagged = 0;
        try
        {
            var validator = new PromptValidator();
            var ps = prompt.PromptSettings;
            foreach (var (name, text) in new[] { ("Personality", ps.Personality), ("SlutModePersonality", ps.SlutModePersonality),
                ("KnowledgeBase", ps.KnowledgeBase), ("ExplicitReaction", ps.ExplicitReaction), ("ContextReactions", ps.ContextReactions) })
            {
                if (string.IsNullOrWhiteSpace(text)) continue;
                var result = validator.Validate(text);
                if (result.Clean) continue;
                flagged++;
                CoreModerationLog.RecordEdit(name, result.MatchedPatterns.Count, "community_prompt");
            }
        }
        catch (Exception ex) { Log.Debug("Community prompt validation failed: {Error}", ex.Message); }
        settings.CompanionPrompt = prompt.PromptSettings;
        settings.CompanionPrompt.UseCustomPrompt = true;
        settings.ActiveCommunityPromptId = id;
        if (flagged > 0) advisory?.Invoke(flagged);
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
