using System;
using Serilog;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Companion.Brain;
using ConditioningControlPanel.Services.Moderation;

namespace ConditioningControlPanel.Services.AIService
{
    /// <summary>
    /// Routes <see cref="IAiService"/> calls to either the cloud-proxy <see cref="AiService"/>
    /// or the local Ollama-backed <see cref="LocalAiService"/> based on
    /// <c>App.Settings.Current.CompanionPrompt.UseLocalAi</c>. Provider switching is live —
    /// no app restart required. Each provider is constructed lazily on first use.
    ///
    /// <para><b>Train 1.</b> This is also where the legacy one-shot methods became adapters. It is the
    /// object <c>App.Ai</c> actually is, so putting the routing here means ONE copy instead of three —
    /// the providers keep their legacy bodies, which is exactly what <c>UseCompanionBrain=false</c>
    /// falls back to. Every branch is gated on <see cref="CompanionBrain.ShouldRoute(CompanionBrain?)"/>
    /// so the kill switch and the "brain failed to construct" case can never drift apart.</para>
    /// </summary>
    public class AiServiceStrategy : IAiService
    {
        /// <summary>Head seam for App.Brain (the legacy adapters' routing target). Unseeded: never routes.</summary>
        public static volatile Func<CompanionBrain?>? BrainProvider;

        private readonly object _lock = new();
        private AiService? _cloud;
        private LocalAiService? _local;
        private OpenAiCompatibleService? _openAi;

        private static AiProviderType Provider =>
            CoreSettings.Service?.Current?.CompanionPrompt?.AiProvider ?? AiProviderType.Cloud;

        private IAiService Active
        {
            get
            {
                switch (Provider)
                {
                    case AiProviderType.Local:
                        return _local ?? TryBuildLocal() ?? (IAiService)UnavailableAiService.Instance;

                    case AiProviderType.OpenAiCompatible:
                        if (_openAi == null)
                        {
                            lock (_lock)
                            {
                                _openAi ??= new OpenAiCompatibleService();
                            }
                        }
                        return _openAi;

                    case AiProviderType.Cloud:
                    default:
                        if (_cloud == null)
                        {
                            lock (_lock)
                            {
                                _cloud ??= new AiService();
                            }
                        }
                        return _cloud;
                }
            }
        }

        // #5197: a LocalAiService constructor that throws (it did on a host saved without http://)
        // used to escape from Active into whatever read App.Ai first - including the MainWindow
        // constructor, so the panel never opened. Now a failed build is logged once, Active reads
        // as "not available" and the build is retried at most every RetryLocalAfter.
        internal static readonly TimeSpan RetryLocalAfter = TimeSpan.FromSeconds(30);
        private DateTime _localFailedAtUtc = DateTime.MinValue;
        private bool _localFailureLogged;

        /// <summary>Test seam: builds the local provider. Defaults to the real constructor.</summary>
        internal Func<LocalAiService> LocalFactory { get; set; } = () => new LocalAiService();

        internal LocalAiService? TryBuildLocal()
        {
            lock (_lock)
            {
                if (_local != null) return _local;
                if (DateTime.UtcNow - _localFailedAtUtc < RetryLocalAfter) return null;
                try
                {
                    _local = LocalFactory();
                    _localFailureLogged = false;
                    return _local;
                }
                catch (Exception ex)
                {
                    _localFailedAtUtc = DateTime.UtcNow;
                    if (!_localFailureLogged)
                    {
                        _localFailureLogged = true;
                        Log.Warning(ex, "AiServiceStrategy: local AI provider failed to start; reading as not available");
                    }
                    return null;
                }
            }
        }

        /// <summary>
        /// What <see cref="Active"/> answers while the local provider cannot be built: not available,
        /// every call a quiet failure. Never throws.
        /// </summary>
        internal sealed class UnavailableAiService : IAiService
        {
            public static readonly UnavailableAiService Instance = new();
            private UnavailableAiService() { }

            public bool IsAvailable => false;
            public int DailyRequestsRemaining => -1;

            public Task<AiReplyResult> SendAsync(System.Collections.Generic.IReadOnlyList<ChatMessage> messages,
                AiCallOptions options, System.Threading.CancellationToken cancellationToken = default)
                => Task.FromResult(AiReplyResult.Failed(AiFailureKind.Unavailable));

            public Task<string> GetBambiReplyAsync(string userInput, bool isUserMessage = false)
                => Task.FromResult(string.Empty);
            public Task<AiReplyResult> GetBambiReplyExAsync(string userInput, bool isUserMessage = false)
                => Task.FromResult(AiReplyResult.Failed(AiFailureKind.Unavailable));
            public Task<string?> GetAwarenessReactionAsync(string detectedName, string category,
                string serviceName = "", string pageTitle = "", TimeSpan? duration = null)
                => Task.FromResult<string?>(null);
            public Task<string?> GetStillOnReactionAsync(string displayName, string category, TimeSpan duration)
                => Task.FromResult<string?>(null);
            public Task<string?> GetKeywordCommentAsync(string keyword, string? promptTemplate = null)
                => Task.FromResult<string?>(null);
            public Task<string?> GetLockScreenReaction(string sentance, int mistakes, int amount, string? promptTemplate = null)
                => Task.FromResult<string?>(null);
            public Task<string?> GetVideoDoneReaction(string title, string? promptTemplate = null)
                => Task.FromResult<string?>(null);
            public void Dispose() { }
        }

        public bool IsAvailable => Active.IsAvailable;

        public int DailyRequestsRemaining => Active.DailyRequestsRemaining;

        public Task<AiReplyResult> SendAsync(System.Collections.Generic.IReadOnlyList<ChatMessage> messages,
            AiCallOptions options, System.Threading.CancellationToken cancellationToken = default)
            => Stamped(() => Active.SendAsync(messages, options, cancellationToken));

        // Every AI line is recorded with the conversation stamp its request started under, so a line
        // that lands on the tube after a persona, identity, account or forget fence never reaches the
        // prompt as history (CompanionBrain.NoteTubeLine).
        private static async Task<string?> Stamped(Func<Task<string?>> call)
        {
            var brain = App.Brain;
            var stamp = brain?.LineStamp();
            var text = await call().ConfigureAwait(false);
            if (brain != null && stamp != null) brain.NoteIssuedLine(text, stamp);
            return text;
        }

        private static async Task<AiReplyResult> Stamped(Func<Task<AiReplyResult>> call)
        {
            var brain = App.Brain;
            var stamp = brain?.LineStamp();
            var result = await call().ConfigureAwait(false);
            if (brain != null && stamp != null && result != null && result.IsAiGenerated)
                brain.NoteIssuedLine(result.Text, stamp);
            return result!;
        }

        // ===================== Train 1 legacy adapters =====================
        //
        // Each of these is: "if the brain is routing, express the moment as a compact event and let
        // the brain own it; otherwise call the provider's untouched legacy body". The legacy bodies
        // are Obsolete too, hence the pragma — this file IS the migration layer.
#pragma warning disable CS0618

        [Obsolete(AiLegacyApi.OneShotObsolete)]
        public Task<string> GetBambiReplyAsync(string userInput, bool isUserMessage = false)
            => Active.GetBambiReplyAsync(userInput, isUserMessage);

        /// <summary>
        /// Routes to <see cref="CompanionBrain.ChatAsync"/> only when
        /// <paramref name="isUserMessage"/> is true.
        ///
        /// <para>The remaining callers of this method pass app-authored prompts, not user speech
        /// (the autonomy nudge, the double-click "random thought", GetBackToMe). Feeding those to
        /// the interactive chat path would mark them <see cref="AiCallOptions.Interactive"/>, which
        /// escalates the user-facing Content Policy Notice — the app must never spend the user's
        /// moderation budget on text the user did not write. They stay stateless in Train 1; giving
        /// them a real home means a per-call instruction in the prompt tail, which is the assembler's
        /// job in Train 2. The real chat box calls <c>CompanionBrain.ChatAsync</c> directly and never
        /// reaches this adapter at all.</para>
        /// </summary>
        [Obsolete(AiLegacyApi.OneShotObsolete)]
        public Task<AiReplyResult> GetBambiReplyExAsync(string userInput, bool isUserMessage = false)
        {
            var brain = BrainProvider?.Invoke();
            if (BrainAdapter.ShouldRouteChat(brain, CompanionBrain.IsEnabled, isUserMessage))
                return brain!.ChatAsync(userInput);

            return Stamped(() => Active.GetBambiReplyExAsync(userInput, isUserMessage));
        }

        [Obsolete(AiLegacyApi.OneShotObsolete)]
        public Task<string?> GetAwarenessReactionAsync(string detectedName, string category,
            string serviceName = "", string pageTitle = "", TimeSpan? duration = null)
        {
            var brain = BrainProvider?.Invoke();
            if (BrainAdapter.ShouldRouteAmbient(brain, CompanionBrain.IsEnabled, IsAvailable, promptTemplate: null))
            {
                return BrainAdapter.ReactAsync(brain!,
                    FrameFormatter.AwarenessEvent(detectedName, category, serviceName, pageTitle, duration));
            }

            return Stamped(() => Active.GetAwarenessReactionAsync(detectedName, category, serviceName, pageTitle, duration));
        }

        [Obsolete(AiLegacyApi.OneShotObsolete)]
        public Task<string?> GetStillOnReactionAsync(string displayName, string category, TimeSpan duration)
        {
            var brain = BrainProvider?.Invoke();
            if (BrainAdapter.ShouldRouteAmbient(brain, CompanionBrain.IsEnabled, IsAvailable, promptTemplate: null))
                return BrainAdapter.ReactAsync(brain!, FrameFormatter.StillOnEvent(displayName, category, duration));

            return Stamped(() => Active.GetStillOnReactionAsync(displayName, category, duration));
        }

        [Obsolete(AiLegacyApi.OneShotObsolete)]
        public Task<string?> GetKeywordCommentAsync(string keyword, string? promptTemplate = null)
        {
            var brain = BrainProvider?.Invoke();
            if (BrainAdapter.ShouldRouteAmbient(brain, CompanionBrain.IsEnabled, IsAvailable, promptTemplate))
                return BrainAdapter.ReactAsync(brain!, FrameFormatter.KeywordEvent(keyword));

            return Stamped(() => Active.GetKeywordCommentAsync(keyword, promptTemplate));
        }

        [Obsolete(AiLegacyApi.OneShotObsolete)]
        public Task<string?> GetLockScreenReaction(string sentance, int mistakes, int amount, string? promptTemplate = null)
        {
            var brain = BrainProvider?.Invoke();
            if (BrainAdapter.ShouldRouteAmbient(brain, CompanionBrain.IsEnabled, IsAvailable, promptTemplate))
                return BrainAdapter.ReactAsync(brain!, FrameFormatter.LockScreenEvent(sentance, mistakes, amount));

            return Stamped(() => Active.GetLockScreenReaction(sentance, mistakes, amount, promptTemplate));
        }

        [Obsolete(AiLegacyApi.OneShotObsolete)]
        public Task<string?> GetVideoDoneReaction(string title, string? promptTemplate = null)
        {
            var brain = BrainProvider?.Invoke();
            if (BrainAdapter.ShouldRouteAmbient(brain, CompanionBrain.IsEnabled, IsAvailable, promptTemplate))
                return BrainAdapter.ReactAsync(brain!, FrameFormatter.VideoDoneEvent(title));

            return Stamped(() => Active.GetVideoDoneReaction(title, promptTemplate));
        }

#pragma warning restore CS0618

        public void Dispose()
        {
            _cloud?.Dispose();
            _local?.Dispose();
            _openAi?.Dispose();
        }

        /// <summary>
        /// Clears the persisted local-AI conversation memory (in-memory + on-disk).
        /// No-op for the cloud provider (it's stateless). Safe to call even when
        /// <see cref="LocalAiService"/> hasn't been constructed yet — we still try
        /// to delete the file so a fresh local provider starts blank.
        /// </summary>
        public void ClearLocalHistory()
        {
            // Construct the local instance if needed only to reach the clear method —
            // alternative is to duplicate the file path here. Cheaper to instantiate.
            TryBuildLocal()?.ClearHistory();
        }

        /// <summary>
        /// Pre-loads the configured Ollama model into memory at startup so the first
        /// chat doesn't pay the cold-start cost. Only runs if the user has local AI
        /// selected — for cloud users this is a no-op. Best-effort, fire-and-forget.
        /// </summary>
        public Task WarmUpLocalAsync()
        {
            if (Provider != Models.AiProviderType.Local) return Task.CompletedTask;

            return TryBuildLocal()?.WarmUpAsync() ?? Task.CompletedTask;
        }
    }
}
