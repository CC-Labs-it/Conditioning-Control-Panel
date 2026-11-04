using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Awareness;
using ConditioningControlPanel.Services.Safety;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

// WPF KeywordTriggerService.cs:1548-1664. A comment must not block later actions.
internal sealed class KeywordAvatarActions
{
    internal Func<bool> AiAvailable = () => App.Ai?.IsAvailable == true;
    internal Func<string, string?, Task<string?>> AiComment = (keyword, prompt) =>
        App.Ai?.GetKeywordCommentAsync(keyword, prompt) ?? Task.FromResult<string?>(null);
    internal Func<string?, string?> PickPhrase = PickCannedPhrase;
    internal Func<(bool Exists, bool Busy)> AvatarState = () =>
    {
        var tube = AvatarTubeWindow.Live;
        return tube == null ? (false, false) : (true, tube.IsSpeaking || !tube.SpokenAudioDone.IsCompleted);
    };
    internal Action<string, bool> ShowLine = (line, ai) =>
        AvatarTubeWindow.Live?.GigglePriority(line, playSound: true, aiGenerated: ai);
    internal Func<int, Task> Delay = Task.Delay;
    internal Task LastComment { get; private set; } = Task.CompletedTask;

    internal void Dispatch(AvatarCommentAction action, string keyword)
    {
        var panicCount = PanicAftermath.PanicCount;
        var available = AiAvailable();
        var prompt = action.PromptTemplate;
        var category = action.FallbackPhraseCategory;
        LastComment = Task.Run(async () =>
        {
            try
            {
                var line = available ? await AiComment(keyword, prompt) : null;
                var fromAi = !string.IsNullOrEmpty(line);
                if (!fromAi) line = PickPhrase(category);
                if (!string.IsNullOrEmpty(line)) await DeliverAsync(line, fromAi, panicCount);
            }
            catch (Exception ex) { Log.Debug(ex, "Keyword avatar comment failed"); }
        });
    }

    private static string? PickCannedPhrase(string? category)
    {
        if (string.IsNullOrEmpty(category)) return null;
        var known = category != "VoiceLine" && CompanionPhraseEditorDialog.CategoryNames.Contains(category);
        var phrases = AwarenessReactionPhrases.Enabled(category, known);
        if (phrases.Length == 0) phrases = CoreMods.GetPhrases(category) ?? Array.Empty<string>();
        return phrases.Length == 0 ? null : phrases[Random.Shared.Next(phrases.Length)];
    }

    private async Task DeliverAsync(string line, bool fromAi, long panicCount)
    {
        for (var attempt = 0; attempt <= 16; attempt++)
        {
            var state = await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (PanicAftermath.PanicCount != panicCount || PanicAftermath.TriggersSilenced())
                    return (Exists: false, Busy: false);
                var avatar = AvatarState();
                if (avatar.Exists && !avatar.Busy) ShowLine(line, fromAi);
                return avatar;
            });
            if (!state.Exists || !state.Busy || attempt == 16) return;
            await Delay(750);
        }
    }
}
