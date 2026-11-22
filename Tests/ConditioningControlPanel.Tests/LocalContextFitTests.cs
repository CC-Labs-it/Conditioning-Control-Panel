using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.AIService;
using ConditioningControlPanel.Services.Companion.Brain;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>Ollama and bring-your-own endpoints get a bigger history fit than the cloud's 4k model
/// (owner 2026-11-22, ccp-bugs #1717: a ~10k character personal prompt shed 26 of 28 history
/// messages under the cloud budget).</summary>
[Collection(PromptPrefixStateCollection.Name)]
public class LocalContextFitTests : IDisposable
{
    public void Dispose() => PromptAssembler.ProviderForTests = null;

    private static List<ChatMessage> LongPromptWithHistory()
    {
        var messages = new List<ChatMessage> { ChatMessage.System(new string('s', 10_180)) };
        for (int i = 0; i < 14; i++)
        {
            messages.Add(ChatMessage.User($"old-{i} " + new string('u', 300)));
            messages.Add(ChatMessage.Assistant($"reply-{i} " + new string('a', 300)));
        }
        messages.Add(ChatMessage.User("NEWEST"));
        return messages;
    }

    [Theory]
    [InlineData(AiProviderType.Local)]
    [InlineData(AiProviderType.OpenAiCompatible)]
    public void Off_the_cloud_a_long_prompt_keeps_most_of_its_history(AiProviderType provider)
    {
        PromptAssembler.ProviderForTests = () => provider;
        var messages = LongPromptWithHistory();

        PromptAssembler.ShedHistoryToContextFit(messages);

        Assert.True(messages.Count >= 20, $"only {messages.Count} messages survived");
        Assert.True(messages.Sum(m => PromptAssembler.ConservativeRealTokens(m.Content))
                    <= PromptAssembler.LocalContextFitTokenBudget);
    }

    [Fact]
    public void The_cloud_keeps_its_4k_budget()
    {
        PromptAssembler.ProviderForTests = () => AiProviderType.Cloud;
        var messages = LongPromptWithHistory();

        PromptAssembler.ShedHistoryToContextFit(messages);

        Assert.True(messages.Sum(m => PromptAssembler.ConservativeRealTokens(m.Content))
                    <= PromptAssembler.ContextFitTokenBudget);
        Assert.True(messages.Count < 10);
    }

    [Fact]
    public void Ollama_is_asked_for_a_window_that_fits_the_local_budget()
    {
        Assert.True(LocalAiService.ComputeNumCtx(null) >= PromptAssembler.LocalContextFitTokenBudget + 512);
        Assert.True(LocalAiService.ComputeNumCtx(null) >= 8192);
    }
}
