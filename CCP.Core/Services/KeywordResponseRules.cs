using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services;

/// <summary>WPF KeywordTriggerService.DispatchMergedAsync and the defensive flat-field fallback.</summary>
public static class KeywordResponseRules
{
    public static string ActionKey(KeywordAction action) => action switch
    {
        VisualEffectAction v => $"VisualEffect:{v.Effect}",
        ExtendSessionAction e => $"ExtendSession:{e.Minutes}",
        ChasterAddTimeAction c => $"ChasterAddTime:{c.Minutes}",
        _ => action.Type,
    };

    public static List<KeywordAction> Actions(IReadOnlyList<KeywordTrigger> fired)
    {
        if (fired.Count == 0) return new();
        var first = fired[0];
        if (fired.Count == 1)
        {
            if (first.Actions is { Count: > 0 }) return first.Actions.Where(a => a != null && a.Enabled).ToList();
            // WPF's legacy HighlightOnly returns before audio, haptics and XP.
            if (first.VisualEffect == KeywordVisualEffect.HighlightOnly) return new() { new HighlightAction() };
            var actions = new List<KeywordAction>();
            if (!string.IsNullOrEmpty(first.AudioFilePath)) actions.Add(new PlayAudioAction
            {
                FilePath = first.AudioFilePath, Volume = first.AudioVolume, PlayCount = first.AudioPlayCount,
                DelayBetweenMs = first.AudioDelayBetweenMs, DuckSystemAudio = first.DuckAudio,
            });
            actions.Add(new VisualEffectAction { Effect = first.VisualEffect });
            actions.Add(new HighlightAction());
            if (first.HapticEnabled) actions.Add(new HapticAction { Intensity = first.HapticIntensity });
            if (first.XPAward > 0) actions.Add(new AddXpAction { Amount = first.XPAward });
            return actions;
        }
        var merged = new List<KeywordAction>();
        var seen = new HashSet<string>();
        foreach (var trigger in fired)
            foreach (var action in trigger.Actions ?? new())
            {
                if (action == null || !action.Enabled) continue;
                // Minutes are display data, not part of WPF's merge slot.
                var key = action is VisualEffectAction ? ActionKey(action) : action.GetType().Name;
                if (seen.Add(key)) merged.Add(action);
            }
        return merged;
    }
}
