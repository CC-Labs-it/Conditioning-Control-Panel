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

    [Theory]
    [InlineData(60, 0, 400)]
    [InlineData(3600, 5000, 5400)]
    [InlineData(3600, 100, 3600)]
    public void CapsLeaveRoomForExistingText(int cap, int length, int expected)
        => Assert.Equal(expected, PersonalityStudioRules.FieldCap(cap, new string('a', length)));
}
