using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using Xunit;

namespace CCP.Avalonia.Tests;

[Collection(RunsAloneCollection.Name)]
public sealed class EnhancementsTextTests
{
    [Theory]
    [InlineData("Pink Hours", "Heures roses", "Focus Hours")]
    [InlineData("Earn XP", "Gagner XP", "Earn focus")]
    [InlineData("Stay pink", "Reste rose", "Stay focused")]
    public void ModVocabularyReceivesEnglishCatalogueText(string english, string localized, string replacement)
    {
        var previous = CoreMods.MakeModAwareProvider;
        try
        {
            CoreMods.MakeModAwareProvider = text => text == english ? replacement : text;
            Assert.Equal(replacement, EnhancementsTabView.SkillText(english, localized));
            // Even a mod with no matching replacement receives the raw WPF catalogue value.
            CoreMods.MakeModAwareProvider = text => text;
            Assert.Equal(english, EnhancementsTabView.SkillText(english, localized));
            CoreMods.MakeModAwareProvider = null;
            Assert.Equal(localized, EnhancementsTabView.SkillText(english, localized));
        }
        finally { CoreMods.MakeModAwareProvider = previous; }
    }
}
