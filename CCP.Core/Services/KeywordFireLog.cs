using System;
using System.Collections.Generic;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services;

/// <summary>
/// The Awareness Engine's live pulse ring buffer (WPF KeywordTriggerService RecordFire step 2 and
/// BuildActionKeySnapshot), shared by both heads. Newest first, bounded, in memory only.
/// The temporal mute half of RecordFire stays with each head's cooldowns.
/// </summary>
public sealed class KeywordFireLog
{
    public const int Capacity = 20;
    private readonly LinkedList<TriggerFireRecord> _fires = new();
    private readonly object _lock = new();

    /// <summary>Snapshot of the most recent fires, newest first ("Last Detected" feed).</summary>
    public IReadOnlyList<TriggerFireRecord> Recent()
    {
        lock (_lock) return new List<TriggerFireRecord>(_fires);
    }

    public void Record(KeywordTrigger trigger, string source, DateTime firedAt)
    {
        var record = new TriggerFireRecord
        {
            Keyword = trigger.Keyword,
            TriggerId = trigger.Id,
            VisualEffect = trigger.VisualEffect,
            Source = source,
            FiredAt = firedAt,
            ActionKeys = ActionKeys(trigger),
        };
        lock (_lock)
        {
            _fires.AddFirst(record);
            while (_fires.Count > Capacity) _fires.RemoveLast();
        }
    }

    /// <summary>
    /// The trigger's enabled actions as display keys, captured at fire time so the feed still
    /// shows the right chips after the trigger is edited or uninstalled. AddXp is left out on
    /// purpose: XP is an internal mechanic, not a user-visible effect.
    /// </summary>
    public static List<string> ActionKeys(KeywordTrigger trigger)
    {
        var keys = new List<string>();
        if (trigger.Actions == null) return keys;

        foreach (var action in trigger.Actions)
        {
            if (action == null || !action.Enabled) continue;
            switch (action)
            {
                case PlayAudioAction:         keys.Add("PlayAudio"); break;
                case HighlightAction:         keys.Add("Highlight"); break;
                case HapticAction:            keys.Add("Haptic"); break;
                case AvatarCommentAction:     keys.Add("AvatarComment"); break;
                case ExtendSessionAction ext: keys.Add($"ExtendSession:{ext.Minutes}"); break;
                case ChasterAddTimeAction ch: keys.Add($"ChasterAddTime:{ch.Minutes}"); break;
                case VisualEffectAction ve:   keys.Add($"VisualEffect:{ve.Effect}"); break;
            }
        }
        return keys;
    }
}
