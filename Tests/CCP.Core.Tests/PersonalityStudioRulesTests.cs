using System.Collections.Generic;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Companion;
using Xunit;

namespace CCP.Core.Tests;

public sealed class PersonalityStudioRulesTests
{
    [Fact]
    public void DraftCopiesOnlyDetachedCharacterText()
    {
        var source = new PersonalityPreset { Id = "owned", Name = "Voice", SampleLines = new() { "Hi" },
            PromptSettings = new() { Personality = "Original" } };
        var active = new CompanionPromptSettings { Personality = "Override", ContextReactions = "Awareness" };
        active.OpenAiCompatibleApiKey = "synthetic-key";
        active.OpenAiCompatibleEndpoint = "https://example.invalid";
        var draft = PersonalityStudioRules.Draft(source, active);
        Assert.NotEqual(source.Id, draft.Id);
        Assert.False(draft.IsBuiltIn);
        Assert.Equal("Override", draft.PromptSettings!.Personality);
        Assert.Equal("Awareness", draft.PromptSettings.ContextReactions);
        Assert.Empty(draft.PromptSettings.OpenAiCompatibleApiKey);
        Assert.Empty(draft.PromptSettings.OpenAiCompatibleEndpoint);
        draft.SampleLines!.Clear();
        draft.PromptSettings.Personality = "Changed";
        Assert.Single(source.SampleLines);
        Assert.Equal("Override", active.Personality);
        Assert.Same(source, PersonalityStudioRules.OwnPreset(source, new[] { source }));
        source.IsBuiltIn = true;
        Assert.Null(PersonalityStudioRules.OwnPreset(source, new[] { source }));
    }

    [Fact]
    public void OwnPresetRequiresAnOwnedIdAndDraftWithoutOverrideUsesSource()
    {
        var source = new PersonalityPreset { Id = "source", Name = "Same name",
            PromptSettings = new() { Personality = "Source voice" } };
        var unrelated = new PersonalityPreset { Id = "other", Name = source.Name };
        Assert.Null(PersonalityStudioRules.OwnPreset(source, new[] { unrelated }));
        Assert.Null(PersonalityStudioRules.OwnPreset(null, new[] { source }));
        Assert.Null(PersonalityStudioRules.OwnPreset(source, null));
        var draft = PersonalityStudioRules.Draft(source);
        Assert.Equal("Source voice", draft.PromptSettings!.Personality);
        Assert.NotSame(source.PromptSettings, draft.PromptSettings);
    }

    // Architectural regression: the Windows entry points must not keep independent rules.
    [Fact]
    public void WindowsStudioForwardsEveryRuleToCore()
    {
        var source = System.IO.File.ReadAllText(System.IO.Path.Combine(
            ConditioningControlPanel.Tests.SourceRoots.RepoRoot,
            "ConditioningControlPanel", "Services", "Companion", "PersonalityStudio.cs"));
        foreach (var forward in new[] { "=> PersonalityStudioRules.Draft(source, activeOverride);",
            "=> PersonalityStudioRules.FieldCap(max, text);",
            "FieldHeadroom = PersonalityStudioRules.FieldHeadroom;",
            "=> PersonalityStudioRules.OwnPreset(source, userPresets);" })
            Assert.Contains(forward, source);
    }

    [Theory]
    [InlineData(60, 0, 400)]
    [InlineData(3600, 5000, 5400)]
    [InlineData(3600, 100, 3600)]
    public void CapsLeaveRoomForExistingText(int cap, int length, int expected)
        => Assert.Equal(expected, PersonalityStudioRules.FieldCap(cap, new string('a', length)));
}
