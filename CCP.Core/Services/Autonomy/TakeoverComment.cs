using System;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.AIService;
using ConditioningControlPanel.Services.Companion.Brain;
using ConditioningControlPanel.Services.Moderation;
using Serilog;

namespace ConditioningControlPanel.Services;

/// <summary>WPF AutonomyService's ambient comment and per-Takeover repeat guard (cb8b2aacc).</summary>
internal sealed class TakeoverComment
{
    internal const string Prompt =
        "Tease the user in one short line, out of nowhere. They are busy in the app and did nothing to ask for it. Be playful.";
    private string? _last;

    internal static async Task<AiReplyResult> GenerateAsync(IAiService ai, CompanionBrain? brain)
    {
        if (BrainAdapter.ShouldRouteAmbient(brain, CompanionBrain.IsEnabled, ai.IsAvailable, promptTemplate: null))
        {
            var line = await BrainAdapter.ReactAsync(brain!, FrameFormatter.TakeoverCommentEvent());
            return new AiReplyResult(line ?? "", true, null);
        }
        return await ai.GetBambiReplyExAsync(Prompt);
    }

    internal bool TryAccept(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (!string.IsNullOrWhiteSpace(_last) && string.Equals(_last.Trim(), text.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            Log.Information("Autonomy: AI comment dropped (same as the last one)");
            return false;
        }
        _last = text;
        return true;
    }
}
