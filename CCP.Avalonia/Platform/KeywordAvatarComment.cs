// WPF KeywordTriggerService.cs:1548-1669, AI-first fallback and bounded busy retry (parity/trunk 4f5dfe318eac).
using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Awareness;
using Serilog;
using ConditioningControlPanel.Services.Safety;
namespace ConditioningControlPanel.Avalonia.Platform;
internal static class KeywordAvatarComment
{
    private static int _generation;
    internal static Func<AvatarTubeWindow?> TubeSource = () => AvatarTubeWindow.Live;
    // Delivery is head-side; tests capture the line without opening a real tube or audio device.
    internal static Action<string, bool, int> Deliver = ShowLine;
    internal static Action<TimeSpan, Action> ScheduleRetry = (delay, fire) => DispatcherTimer.RunOnce(fire, delay);
    internal static void CancelPending() => Interlocked.Increment(ref _generation);
    internal static async Task DispatchAsync(AvatarCommentAction action, KeywordTrigger trigger)
    {
        if (PanicAftermath.TriggersSilenced()) return; // WPF KeywordTriggerService.cs:1263
        var generation = Volatile.Read(ref _generation);
        try
        {
            var ai = App.Ai;
            string? line = null;
            var fromAi = false;
            if (ai?.IsAvailable == true)
            {
                line = await ai.GetKeywordCommentAsync(trigger.Keyword, action.PromptTemplate);
                fromAi = !string.IsNullOrEmpty(line);
            }
            if (string.IsNullOrEmpty(line))
            {
                var category = action.FallbackPhraseCategory;
                if (!string.IsNullOrEmpty(category))
                {
                    var phrases = AwarenessReactionPhrases.Enabled(category);
                    if (phrases.Length == 0) phrases = CoreMods.GetPhrases(category) ?? Array.Empty<string>();
                    if (phrases.Length > 0) line = phrases[Random.Shared.Next(phrases.Length)];
                }
            }
            if (!string.IsNullOrEmpty(line) && generation == Volatile.Read(ref _generation))
                Deliver(line, fromAi, generation);
        }
        catch (Exception ex) { Log.Debug(ex, "Keyword avatar comment failed"); }
    }
    private static void ShowLine(string line, bool aiGenerated, int generation) =>
        Dispatcher.UIThread.Post(() => TryShowLine(line, aiGenerated, generation, 0));
    private static void TryShowLine(string line, bool aiGenerated, int generation, int attempt)
    {
        if (generation != Volatile.Read(ref _generation) || PanicAftermath.TriggersSilenced()) return;
        var tube = TubeSource();
        if (tube == null) return;
        if (tube.IsSpeaking || !tube.SpokenAudioDone.IsCompleted)
        {
            if (attempt >= 16) return;
            ScheduleRetry(TimeSpan.FromMilliseconds(750),
                () => TryShowLine(line, aiGenerated, generation, attempt + 1));
            return;
        }
        tube.GigglePriority(line, playSound: true, aiGenerated: aiGenerated);
        // ponytail: v2's arbiter/ledger is not ported, so no external-line ledger write yet.
    }
}
