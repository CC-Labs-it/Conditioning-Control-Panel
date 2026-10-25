using System.Collections.Generic;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Companion;
using Xunit;
namespace CCP.Core.Tests;
public sealed class PersonalityStudioRulesTests
{
    [Fact]
    public void DraftCopiesCharacterOnlyAndOwnIdentityIsById()
    {
        var source = new PersonalityPreset { Id = "source", Name = "Test", IsBuiltIn = true,
            SampleLines = new List<string> { "sample" }, PromptSettings = new CompanionPromptSettings { Personality = "original" } };
        var edited = new CompanionPromptSettings { Personality = "override", ContextReactions = "awareness", OutputRules = "style" };
        var draft = PersonalityStudioRules.Draft(source, edited);
        Assert.NotEqual(source.Id, draft.Id);
        Assert.False(draft.IsBuiltIn);
        Assert.Equal("override", draft.PromptSettings.Personality);
        Assert.Equal("awareness", draft.PromptSettings.ContextReactions);
        draft.SampleLines!.Add("new sample");
        Assert.Single(source.SampleLines);
        draft.PromptSettings.Personality = "changed";
        Assert.Equal("override", edited.Personality);
        Assert.Null(PersonalityStudioRules.OwnPreset(source, new[] { source }));
        Assert.Same(draft, PersonalityStudioRules.OwnPreset(draft, new[] { draft }));
        Assert.Null(PersonalityStudioRules.OwnPreset(new PersonalityPreset { Id = "other", Name = draft.Name }, new[] { draft }));
    }
    [Fact]
    public void CapOfferAndDayStampMatchWpf()
    {
        Assert.Equal(1, CompanionCapRules.CapRowOffers(0));
        Assert.Equal(2, CompanionCapRules.CapRowOffers(1));
        Assert.Null(CompanionCapRules.CapRowOffers(2));
        var stamps = new Dictionary<string, string>();
        var today = CompanionCapRules.DayStamp(new System.DateTime(2026, 10, 25));
        Assert.True(CompanionCapRules.Owed(stamps, today));
        stamps[CompanionCapRules.KeyCompanionCap] = today;
        Assert.False(CompanionCapRules.Owed(stamps, today));
        Assert.True(CompanionCapRules.Owed(stamps, "2026-10-26"));
        Assert.EndsWith("rid=27604786", CompanionCapRules.PlanUrl(1));
        Assert.EndsWith("rid=27681505", CompanionCapRules.PlanUrl(2));
    }
    [Theory]
    [InlineData(60, 4, 404)]
    [InlineData(3600, 4000, 4400)]
    [InlineData(3600, 10, 3600)]
    public void LongExistingTextAlwaysHasEditingHeadroom(int cap, int length, int expected)
        => Assert.Equal(expected, PersonalityStudioRules.FieldCap(cap, new string('x', length)));
}
