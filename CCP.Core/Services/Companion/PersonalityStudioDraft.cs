using System;
using System.Linq;
using ConditioningControlPanel.Models;
namespace ConditioningControlPanel.Services.Companion;

/// <summary>WPF PersonalityStudio.Draft: detached character text, without credentials or permissions.</summary>
public static class PersonalityStudioDraft
{
    public static PersonalityPreset Create(PersonalityPreset source, CompanionPromptSettings? activeOverride = null)
    {
        var p = activeOverride ?? source.PromptSettings ?? new CompanionPromptSettings();
        return new PersonalityPreset {
            Id = Guid.NewGuid().ToString("N"), Name = source.Name, Description = source.Description,
            IsBuiltIn = false, RequiresExplicitAcknowledgement = source.RequiresExplicitAcknowledgement,
            SampleLines = source.SampleLines?.ToList(), PromptSettings = new CompanionPromptSettings {
                UseCustomPrompt = true, Personality = p.Personality, ExplicitReaction = p.ExplicitReaction,
                SlutModePersonality = p.SlutModePersonality, KnowledgeBase = p.KnowledgeBase,
                ContextReactions = p.ContextReactions, OutputRules = p.OutputRules }
        };
    }
}
