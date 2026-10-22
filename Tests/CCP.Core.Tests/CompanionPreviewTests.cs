using System.Collections.Generic;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Companion;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>Moved from the WPF CustomiseWindowRulesTests with the rule.</summary>
public sealed class CompanionPreviewTests
{
    // ---- companion preview (a mod not in use) ----

    [Fact]
    public void PreviewCountsEveryStockLookWhenTheModDeclaresNone()
    {
        Assert.Equal(7, CompanionPreview.CountLooks(new ModManifest(), singleEmote: false));
    }

    [Fact]
    public void PreviewCountsSupportedAndCustomLooks()
    {
        var m = new ModManifest
        {
            SupportedAvatarSets = new() { 1, 3, 8 },
            CustomAvatarSets = new() { new CustomAvatarSet { SetNumber = 8 }, new CustomAvatarSet { SetNumber = 9 } }
        };
        Assert.Equal(3, CompanionPreview.CountLooks(m, singleEmote: false));
        Assert.Equal(1, CompanionPreview.CountLooks(m, singleEmote: true));
    }

    [Fact]
    public void PreviewUsesTheModsOwnPersonalities()
    {
        var m = new ModManifest { Name = "Circe's Lock", Identity = new ModIdentity { CompanionName = "Circe" } };
        var info = CompanionPreview.Build(m, new List<ModPersonality>
        {
            new() { Id = "a", Name = "A", SampleLines = new() { "The key is safe." } },
            new() { Id = "b", Name = "B" }
        }, singleEmote: false, neutral: false);
        Assert.Equal("Circe", info.Name);
        Assert.Equal(2, info.Personalities);
        Assert.Equal("The key is safe.", info.SampleLine);
    }

    [Fact]
    public void PreviewFallsBackToTheStockSetAndTheModName()
    {
        var info = CompanionPreview.Build(new ModManifest { Name = "CCP Default" }, null, singleEmote: false, neutral: true);
        Assert.Equal("CCP Default", info.Name);
        // The neutral picker hides the four niche personas (owner, 2026-09-25).
        Assert.Equal(PersonalityPresets.GetAllBuiltIn().Count - PersonalityPresets.HiddenInNeutralIds.Length, info.Personalities);
        Assert.Equal(PersonalitySamples.For(PersonalityPresets.GetNeutralDefault())[0], info.SampleLine);
    }
}
