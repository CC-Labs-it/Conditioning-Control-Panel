using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Models.AiEnrichment;
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
    public void Dispose()
    {
        PromptAssembler.ProviderForTests = null;
        OwnEndpointContextFit.ResetForTests();
    }

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

    // ---- a bring-your-own server with a 4k context refuses the 7,000 fit ----

    [Theory]
    [InlineData(400, "{\"error\":{\"code\":400,\"message\":\"the request exceeds the available context size, try increasing it\",\"type\":\"exceed_context_size_error\",\"n_prompt_tokens\":6120,\"n_ctx\":4096}}")]
    [InlineData(400, "{\"object\":\"error\",\"message\":\"This model's maximum context length is 4096 tokens. However, you requested 6200 tokens (6100 in the messages, 100 in the completion).\",\"type\":\"BadRequestError\",\"code\":400}")]
    [InlineData(400, "{\"error\":{\"message\":\"...\",\"code\":\"context_length_exceeded\"}}")]
    [InlineData(400, "{\"error\":\"Trying to keep the first 6100 tokens when context the overflows. However, the model is loaded with context length of only 4096 tokens, which is not enough.\"}")]
    [InlineData(500, "{\"error\":{\"code\":500,\"message\":\"input is too large to process. increase the physical batch size\"}}")]
    [InlineData(422, "{\"error\":\"Input validation error: `inputs` tokens + `max_new_tokens` must be <= 4096. Given: 6100 `inputs` tokens and 200 `max_new_tokens`\"}")]
    [InlineData(400, "{\"detail\":\"Prompt is too long for this context size\"}")]
    public void Context_size_refusals_are_recognised(int status, string body)
        => Assert.True(OwnEndpointContextFit.IsContextSizeRefusal(status, body));

    [Theory]
    [InlineData(400, "{\"error\":{\"message\":\"model 'llama3' not found\"}}")]
    [InlineData(401, "{\"error\":\"maximum context length\"}")]
    [InlineData(429, "{\"error\":\"Rate limit reached: too many tokens per minute\"}")]
    [InlineData(500, "")]
    public void Other_errors_are_not_context_refusals(int status, string body)
        => Assert.False(OwnEndpointContextFit.IsContextSizeRefusal(status, body));

    private const string LlamaCppRefusal =
        "{\"error\":{\"code\":400,\"message\":\"the request exceeds the available context size, try increasing it\",\"type\":\"exceed_context_size_error\"}}";

    private static List<MessageDto> WireOf(List<ChatMessage> messages)
        => messages.Select(m => new MessageDto(m.Role, m.Content)).ToList();

    [Fact]
    public void A_context_refusal_sheds_to_the_cloud_fit_and_retries_once()
    {
        PromptAssembler.ProviderForTests = () => AiProviderType.OpenAiCompatible;
        var chat = LongPromptWithHistory();
        PromptAssembler.ShedHistoryToContextFit(chat);
        var wire = WireOf(chat);
        var system = wire[0];
        const string key = "http://127.0.0.1:8080/v1/";

        Assert.True(OwnEndpointContextFit.TryShedForRetry(400, LlamaCppRefusal, alreadyRetried: false, wire, 1, key));

        Assert.Same(system, wire[0]);
        Assert.Equal("NEWEST", wire[^1].Content);
        Assert.True(wire.Sum(m => PromptAssembler.ConservativeRealTokens(m.Content)) <= PromptAssembler.ContextFitTokenBudget);

        // The second refusal of the same request is a hard failure: no retry loop.
        Assert.False(OwnEndpointContextFit.TryShedForRetry(400, LlamaCppRefusal, alreadyRetried: true, wire, 1, key));
    }

    [Fact]
    public void Another_error_never_retries_and_never_marks_the_endpoint()
    {
        var wire = WireOf(LongPromptWithHistory());
        var before = wire.Count;
        const string key = "http://127.0.0.1:8080/v1/";

        Assert.False(OwnEndpointContextFit.TryShedForRetry(400, "{\"error\":\"model not found\"}", false, wire, 1, key));

        Assert.Equal(before, wire.Count);
        Assert.False(OwnEndpointContextFit.IsSmall(key));
    }

    [Fact]
    public void A_refused_endpoint_builds_to_the_cloud_fit_for_the_rest_of_the_session()
    {
        PromptAssembler.ProviderForTests = () => AiProviderType.OpenAiCompatible;
        var key = OpenAiCompatibleService.CurrentEndpointKey();
        Assert.Equal(PromptAssembler.LocalContextFitTokenBudget, PromptAssembler.ContextFitTokenBudgetFor(AiProviderType.OpenAiCompatible));

        var wire = WireOf(LongPromptWithHistory());
        OwnEndpointContextFit.TryShedForRetry(400, LlamaCppRefusal, false, wire, 1, key);

        Assert.True(OwnEndpointContextFit.IsSmall(key));
        Assert.Equal(PromptAssembler.ContextFitTokenBudget, PromptAssembler.ContextFitTokenBudgetFor(AiProviderType.OpenAiCompatible));
        var next = LongPromptWithHistory();
        PromptAssembler.ShedHistoryToContextFit(next);
        Assert.True(next.Sum(m => PromptAssembler.ConservativeRealTokens(m.Content)) <= PromptAssembler.ContextFitTokenBudget);
        // Ollama sizes its own window and keeps the bigger fit.
        Assert.Equal(PromptAssembler.LocalContextFitTokenBudget, PromptAssembler.ContextFitTokenBudgetFor(AiProviderType.Local));
    }

    [Fact]
    public void Nothing_to_shed_means_no_retry()
    {
        var wire = new List<MessageDto> { new("system", new string('s', 12_000)), new("user", "hi") };
        Assert.False(OwnEndpointContextFit.TryShedForRetry(400, LlamaCppRefusal, false, wire, 1, "http://x/v1/"));
        Assert.Equal(2, wire.Count);
    }
}
