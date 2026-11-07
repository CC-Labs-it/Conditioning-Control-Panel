using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Companion;
using Xunit;
namespace CCP.Core.Tests;
public sealed class PersonalityStudioDraftTests
{
    [Fact]
    public void DraftCopiesCharacterTextWithoutAliasingOrProviderCredentials()
    {
        var source = new PersonalityPreset { Id = "source", Name = "Name", IsBuiltIn = true, SampleLines = new() { "sample" },
            PromptSettings = new CompanionPromptSettings { Personality = "source voice" } };
        var active = new CompanionPromptSettings { Personality = "override", KnowledgeBase = "background", ContextReactions = "awareness", OpenAiCompatibleApiKey = "test-only-value", AiOllamaHost = "https://example.org" };
        var draft = PersonalityStudioDraft.Create(source, active);
        Assert.NotEqual(source.Id, draft.Id); Assert.False(draft.IsBuiltIn); Assert.True(draft.PromptSettings!.UseCustomPrompt);
        Assert.Equal("override", draft.PromptSettings.Personality); Assert.Equal("awareness", draft.PromptSettings.ContextReactions);
        Assert.Equal("background", draft.PromptSettings.KnowledgeBase); Assert.NotSame(active, draft.PromptSettings);
        Assert.Empty(draft.PromptSettings.OpenAiCompatibleApiKey); Assert.Equal(new CompanionPromptSettings().AiOllamaHost, draft.PromptSettings.AiOllamaHost);
        draft.SampleLines!.Clear(); draft.PromptSettings.Personality = "edit";
        Assert.Single(source.SampleLines!); Assert.Equal("override", active.Personality); Assert.Equal("source voice", source.PromptSettings.Personality);
    }
}
