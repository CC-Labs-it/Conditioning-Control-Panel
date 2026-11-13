using System;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.AIService;
using ConditioningControlPanel.Services.Moderation;

namespace ConditioningControlPanel.Services.Awareness
{
    /// <summary>
    /// The v2 awareness LLM path: one cut frame in, at most one line out (doc 02 §3.1).
    ///
    /// <para><b>It is only ever reached from a frame.</b> There is no timer, no poll and no "check in
    /// with the AI every N seconds" entry point here, and doc 02 §7 item 6 says there must never be
    /// one. The single public method takes a <see cref="ContextFrame"/>, which by construction only
    /// exists because something happened.</para>
    ///
    /// <para><b>It does not pace itself, on purpose.</b> Cooldowns, the hourly line budget and the
    /// "burn the cooldown on delivery, not on attempt" rule all live in the one shared ledger the
    /// arbiter owns (<see cref="ReactionCooldownLedger"/>). A second cooldown dialect in here is
    /// exactly how the two-mouths bug happened the first time (doc 02 §1.5), so this class assumes its
    /// caller already decided the moment was worth spending on, and returning a line is NOT a delivery
    /// — the arbiter records that when the line actually reaches a bubble.</para>
    ///
    /// <para><b>Moderation is untouched.</b> The full Layer-1 spine — <c>CheckInput</c> on the frame
    /// message, <c>CheckOutput</c> on the reply, <c>ModerationLog</c> — runs inside
    /// <see cref="IAiService.SendAsync"/> exactly as it does for chat, which is why the frame is sent
    /// as a user-role message rather than being folded into the system prompt: a system-only request
    /// would leave the input check with nothing to look at. <see cref="AiCallOptions.Interactive"/>
    /// stays false, so a blocked ambient line is logged for compliance but never escalates the
    /// user-facing Content Policy Notice for text the user did not type.</para>
    /// </summary>
    public sealed partial class AwarenessReactionService
    {
        /// <summary>The output contract's hard cap. Anything longer is trimmed rather than dropped.</summary>
        public const int MaxLineLength = 140;

        /// <summary>The silence token the contract offers and doc 02 §3.4 asks us to honour.</summary>
        public const string PassToken = "[PASS]";

        /// <summary>Prefix of the optional past-tense second line (doc 02 §4.3).</summary>
        public const string CallbackPrefix = "CALLBACK:";

        private readonly Func<IAiService?> _transport;
        private readonly AwarenessPromptBuilder _builder;
        private readonly Func<bool> _isMachineLocal;
        private readonly Func<bool> _isEnabled;

        /// <param name="transport">Defaults to <c>App.Ai</c>, resolved per call so a provider switch takes effect live.</param>
        /// <param name="builder">Prompt builder. Defaults to a fresh one (it caches its own prefixes).</param>
        /// <param name="isMachineLocal">
        /// Whether the active provider runs on this machine, which is what licenses the fuller
        /// projection. Defaults to "the provider is Ollama" and nothing else: an OpenAI-compatible
        /// endpoint MAY be a localhost server, but it may equally be a hosted one, and the projection
        /// layer does not get to guess (addendum D — when privacy cannot answer, take the narrow path).
        /// </param>
        /// <param name="isEnabled">
        /// The v2 kill switch / awareness toggle / consent gate, defaulting to
        /// <see cref="AwarenessObserver.IsEnabled"/>. The observer and the arbiter already refuse to
        /// reach here when awareness is off, so this is defence in depth for future callers (a privacy
        /// panel preview, say) rather than the primary gate — but a "no consent" that only holds in
        /// one of three places is not a consent gate.
        /// </param>
        public AwarenessReactionService(
            Func<IAiService?>? transport = null,
            AwarenessPromptBuilder? builder = null,
            Func<bool>? isMachineLocal = null,
            Func<bool>? isEnabled = null)
        {
            _transport = transport ?? (() => App.Ai);
            _builder = builder ?? new AwarenessPromptBuilder();
            _isMachineLocal = isMachineLocal ?? DefaultIsMachineLocal;
            _isEnabled = isEnabled ?? (() => AwarenessObserver.IsEnabled);
        }

        /// <summary>The prompt builder in use. Exposed so the privacy panel can show the real prompt.</summary>
        public AwarenessPromptBuilder Prompts => _builder;

        /// <summary>
        /// Asks the model for a reaction to one frame. Never throws: awareness degrading to silence is
        /// a missed joke, awareness degrading to an unhandled exception on a background path is a crash
        /// log (the observer runs on a timer).
        /// </summary>
        public async Task<AwarenessReaction> GetAwarenessReactionAsync(
            ContextFrame? frame, CancellationToken cancellationToken = default)
        {
            if (frame == null) return AwarenessReaction.None("null-frame");
            if (!_isEnabled()) return AwarenessReaction.None("v2-off");

            var transport = _transport();
            if (transport == null) return AwarenessReaction.None("no-transport");

            AwarenessPrompt prompt;
            try
            {
                prompt = _builder.Build(frame, local: _isMachineLocal());
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "[AWARE] prompt build failed");
                return AwarenessReaction.None("prompt-failed");
            }

            App.Logger?.Debug("{Line}", prompt.LogLine);

            AiReplyResult result;
            try
            {
                var options = AiCallOptions.Reaction with
                {
                    MaxTokens = AwarenessPromptBuilder.ResponseMaxTokens
                };
                result = await transport.SendAsync(prompt.Messages, options, cancellationToken)
                                        .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return AwarenessReaction.None("cancelled");
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "[AWARE] reaction call failed");
                return AwarenessReaction.None("transport-failed");
            }

            if (result == null) return AwarenessReaction.None("no-result");

            if (result.Refusal != null)
            {
                App.Logger?.Debug("[AWARE] reaction refused by the moderation spine");
                return new AwarenessReaction(string.Empty, null, Passed: false, IsAiGenerated: false,
                    Refusal: result.Refusal, Reason: "refused");
            }

            if (!result.IsAiGenerated) return AwarenessReaction.None("no-ai");

            var parsed = Parse(result.Text);
            App.Logger?.Debug("[AWARE] reaction key={Key} tier={Tier} outcome={Outcome} len={Len}",
                AwarenessText.SanitizeId(prompt.CardKey), prompt.Tier, parsed.Reason, parsed.Line.Length);
            return parsed;
        }

        private static bool DefaultIsMachineLocal()
        {
            try
            {
                return App.Settings?.Current?.CompanionPrompt?.AiProvider == AiProviderType.Local;
            }
            catch { return false; }
        }
    }
}
