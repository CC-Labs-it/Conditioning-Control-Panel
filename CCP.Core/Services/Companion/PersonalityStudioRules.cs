using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.Companion;

// WPF PersonalityStudio: detached character text only; settings/service owns persistence.
internal static class PersonalityStudioRules
{
    internal static PersonalityPreset Draft(PersonalityPreset source, CompanionPromptSettings? activeOverride = null)
    {
        var p = activeOverride ?? source.PromptSettings ?? new CompanionPromptSettings();
        return new PersonalityPreset
        {
            Id = Guid.NewGuid().ToString("N"), Name = source.Name, Description = source.Description,
            IsBuiltIn = false, RequiresExplicitAcknowledgement = source.RequiresExplicitAcknowledgement,
            SampleLines = source.SampleLines?.ToList(),
            // Only character text belongs in a preset. Never copy API keys, hosts or effect permissions.
            PromptSettings = new CompanionPromptSettings
            {
                UseCustomPrompt = true, Personality = p.Personality, ExplicitReaction = p.ExplicitReaction,
                SlutModePersonality = p.SlutModePersonality, KnowledgeBase = p.KnowledgeBase,
                ContextReactions = p.ContextReactions, OutputRules = p.OutputRules
            }
        };
    }

    /// <summary>
    /// A box's length cap. Never under what it opens with: a mod persona or an old custom prompt
    /// longer than the cap opened as a box that refused every keystroke (#1417).
    /// </summary>
    internal static int FieldCap(int max, string? text)
        => Math.Max(max, (text?.Length ?? 0) + FieldHeadroom);

    internal const int FieldHeadroom = 400;


    /// <summary>
    /// The user's own preset behind <paramref name="source"/>, or null for a built-in or mod one.
    /// Matched by id against the user's list, so a built-in that happens to share a name never counts.
    /// </summary>
    internal static PersonalityPreset? OwnPreset(PersonalityPreset? source, IEnumerable<PersonalityPreset>? userPresets)
        => source == null || source.IsBuiltIn ? null : userPresets?.FirstOrDefault(p => p.Id == source.Id);

}
