using System;
using System.Collections.Generic;
using System.IO;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Companion;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// ccp-bugs #1481: the studio's name box clipped what was typed, every Save added another copy,
/// and nothing in the UI could delete a custom personality.
/// </summary>
public class PersonalityStudioOwnPresetTests
{
    [Fact]
    public void OwnPreset_IsTheUsersPreset_MatchedById()
    {
        var mine = new PersonalityPreset { Id = "mine", Name = "Mine", IsBuiltIn = false };
        var list = new List<PersonalityPreset> { new() { Id = "other", Name = "Other" }, mine };
        Assert.Same(mine, PersonalityStudio.OwnPreset(new PersonalityPreset { Id = "mine", IsBuiltIn = false }, list));
    }

    [Fact]
    public void OwnPreset_IsNull_ForABuiltInOrAPresetNotInTheList()
    {
        var list = new List<PersonalityPreset> { new() { Id = "mine", Name = "Mine" } };
        Assert.Null(PersonalityStudio.OwnPreset(new PersonalityPreset { Id = "mine", IsBuiltIn = true }, list));
        Assert.Null(PersonalityStudio.OwnPreset(new PersonalityPreset { Id = "mod-persona", IsBuiltIn = false }, list));
        Assert.Null(PersonalityStudio.OwnPreset(null, list));
        Assert.Null(PersonalityStudio.OwnPreset(new PersonalityPreset { Id = "mine" }, null));
    }

    [Fact]
    public void Studio_UpdatesItsOwnPresetInPlace_AndOffersDelete()
    {
        var src = Source("ConditioningControlPanel", "Services", "Companion", "PersonalityStudio.cs");
        Assert.Contains("own.PromptSettings = draft.PromptSettings;", src);
        Assert.Contains("service.SetActivePreset(own.Id)", src);
        Assert.Contains("service.DeletePreset(own.Id)", src);
        // A community or hand-edited prompt fills the boxes: saving that in place would overwrite the preset.
        Assert.Contains("var own = overridden ? null : OwnPreset(source, settings.UserPersonalityPresets);", src);
        // The name box is one line that sizes to its text, never the fixed 34 px wrap.
        Assert.Contains("Field(form, L(\"name\"), source.Name, 60, SingleLine)", src);
        Assert.DoesNotContain("source.Name, 60, 34)", src);
    }

    [Fact]
    public void DeletingTheActivePreset_TellsThePickers()
    {
        var src = Source("ConditioningControlPanel", "Services", "Companion", "PersonalityService.cs");
        Assert.Contains("if (wasActive) PersonalityChanged?.Invoke(this, GetActivePreset());", src);
    }

    [Theory]
    [InlineData("companion_studio_intro_own")]
    [InlineData("companion_studio_update")]
    [InlineData("companion_studio_delete")]
    [InlineData("companion_studio_delete_confirm")]
    public void EveryLanguageCarriesTheNewStudioKeys(string key)
    {
        var dir = Path.Combine(Root(), "ConditioningControlPanel", "Localization", "Languages");
        var files = Directory.GetFiles(dir, "*.json");
        Assert.Equal(10, files.Length);
        foreach (var f in files)
            Assert.Contains($"\"{key}\":", File.ReadAllText(f));
    }

    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "ConditioningControlPanel.csproj")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string Source(params string[] parts) => File.ReadAllText(Path.Combine(Root(), Path.Combine(parts)));
}
