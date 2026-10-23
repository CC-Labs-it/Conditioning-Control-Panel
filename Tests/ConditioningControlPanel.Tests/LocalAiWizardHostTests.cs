using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.AIService;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The local AI wizard said "all set" but never wrote the host it verified, so a blank Host box
/// stayed blank. A successful run now writes provider, model and host.
/// </summary>
public class LocalAiWizardHostTests
{
    [Fact]
    public void Verified_host_fills_a_blank_host()
    {
        var prompt = new CompanionPromptSettings { AiOllamaHost = "", AiProvider = AiProviderType.Cloud };

        OllamaSetupService.ApplyVerifiedSetup(prompt, OllamaSetupService.DefaultHost, "qwen3.5:latest");

        Assert.Equal("http://localhost:11434/", prompt.AiOllamaHost);
        Assert.Equal(AiProviderType.Local, prompt.AiProvider);
        Assert.Equal("qwen3.5:latest", prompt.AiModel);
    }

    [Fact]
    public void Verified_host_replaces_a_host_the_wizard_did_not_check()
    {
        var prompt = new CompanionPromptSettings { AiOllamaHost = "http://192.168.1.9:11434" };

        OllamaSetupService.ApplyVerifiedSetup(prompt, " http://localhost:11434 ", "m");

        Assert.Equal("http://localhost:11434/", prompt.AiOllamaHost);
    }

    [Fact]
    public void Blank_verified_host_leaves_the_setting_alone()
    {
        var prompt = new CompanionPromptSettings { AiOllamaHost = "http://box:11434/" };

        OllamaSetupService.ApplyVerifiedSetup(prompt, "  ", "m");

        Assert.Equal("http://box:11434/", prompt.AiOllamaHost);
    }
}
