using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Models.AiEnrichment;
using ConditioningControlPanel.Services.Companion.Brain;

namespace ConditioningControlPanel.Services.AIService
{
    /// <summary>
    /// A bring-your-own endpoint gets the 7,000-token history fit (owner 2026-11-22,
    /// <see cref="PromptAssembler.LocalContextFitTokenBudget"/>), but many such servers run a 4k
    /// context (llama.cpp <c>-c 4096</c>, older koboldcpp and LM Studio) and refuse a bigger request
    /// outright. When one refuses for context size, the request sheds history down to the cloud's
    /// <see cref="PromptAssembler.ContextFitTokenBudget"/> and is sent ONCE more, and the endpoint is
    /// remembered as small for the rest of the session so the next prompts are built to the small fit.
    /// </summary>
    internal static class OwnEndpointContextFit
    {
        private static readonly ConcurrentDictionary<string, byte> SmallEndpoints = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Fragments of the context-size refusals the common servers send, lower case:
        /// llama.cpp ("the request exceeds the available context size", type exceed_context_size_error,
        /// older "input is too large to process"), OpenAI / vLLM ("This model's maximum context length is
        /// 4096 tokens", code context_length_exceeded), LM Studio ("loaded with context length of only"),
        /// koboldcpp and TGI wordings, and the generic "prompt is too long".</summary>
        private static readonly string[] ContextSizeMarkers =
        {
            "exceed_context_size",
            "exceeds the available context size",
            "context_length_exceeded",
            "maximum context length",
            "context length exceeded",
            "context size exceeded",
            "exceeds the context",
            "exceeds context",
            "context length of only",
            "longer than the context",
            "greater than the context length",
            "input is too large to process",
            "prompt is too long",
            "prompt too long",
            "max_new_tokens` must be <=",
            "reduce the length of the messages",
        };

        /// <summary>True when an error answer reads as "the request is bigger than my context".</summary>
        internal static bool IsContextSizeRefusal(int status, string? body)
        {
            if (status != 400 && status != 413 && status != 422 && status != 500) return false;
            if (string.IsNullOrEmpty(body)) return false;
            var lower = body.ToLowerInvariant();
            return ContextSizeMarkers.Any(lower.Contains);
        }

        /// <summary>True when this endpoint refused a request for context size earlier this session.</summary>
        internal static bool IsSmall(string? endpointKey)
            => !string.IsNullOrEmpty(endpointKey) && SmallEndpoints.ContainsKey(endpointKey);

        /// <summary>The fit budget the assembler uses for a bring-your-own endpoint.</summary>
        internal static int BudgetFor(string? endpointKey)
            => IsSmall(endpointKey) ? PromptAssembler.ContextFitTokenBudget : PromptAssembler.LocalContextFitTokenBudget;

        /// <summary>
        /// The one-retry rule. Called with an error answer: when it is a context-size refusal and this
        /// request has not been retried yet, the endpoint is remembered as small (logged once per
        /// endpoint), history after the first <paramref name="protectedHead"/> messages is shed oldest
        /// first to the cloud fit (the newest message is always kept), and true asks for one more send.
        /// False when it is another error, the request was already retried, or nothing could be shed.
        /// </summary>
        internal static bool TryShedForRetry(int status, string? body, bool alreadyRetried,
            List<MessageDto> messages, int protectedHead, string? endpointKey)
        {
            if (alreadyRetried || messages == null || !IsContextSizeRefusal(status, body)) return false;

            if (!string.IsNullOrEmpty(endpointKey) && SmallEndpoints.TryAdd(endpointKey, 0))
            {
                App.Logger?.Warning(
                    "[AI-PROMPT] own endpoint refused the request for context size (HTTP {Status}); history fits ~{Budget} real tokens for the rest of this session",
                    status, PromptAssembler.ContextFitTokenBudget);
            }

            var head = Math.Max(0, protectedHead);
            var budget = PromptAssembler.ContextFitTokenBudget;
            int Estimate() => messages.Sum(m => PromptAssembler.ConservativeRealTokens(m.Content));

            var shed = 0;
            while (messages.Count > head + 1 && Estimate() > budget)
            {
                messages.RemoveAt(head);
                shed++;
            }
            return shed > 0;
        }

        internal static void ResetForTests() => SmallEndpoints.Clear();
    }
}
