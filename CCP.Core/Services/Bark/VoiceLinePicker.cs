using System;
using System.Collections.Generic;
using System.Linq;

namespace ConditioningControlPanel.Services.Bark;

/// <summary>WPF BarkService.PickVoiceLine: a matching text/clip from the current manifest,
/// avoiding the immediately previous variant. The owner serializes access with its rules lock.</summary>
public sealed class VoiceLinePicker
{
    private readonly Random _random;
    private readonly Dictionary<string, int> _last = new(StringComparer.OrdinalIgnoreCase);

    public VoiceLinePicker(Random? random = null) => _random = random ?? new Random();

    public (string Text, string? Audio)? Pick(BarkRuleSet rules, string? ruleId, Func<string?, string?> resolveAudio)
    {
        if (string.IsNullOrWhiteSpace(ruleId)) return null;
        var pool = rules.AllRules.FirstOrDefault(r => string.Equals(r.Id, ruleId, StringComparison.OrdinalIgnoreCase))?.VariantPool;
        if (pool == null || pool.Count == 0) return null;
        var index = _random.Next(pool.Count);
        if (pool.Count > 1 && _last.TryGetValue(ruleId, out var last) && index == last)
            index = (index + 1) % pool.Count;
        _last[ruleId] = index;
        var variant = pool[index];
        return (variant.Text, resolveAudio(variant.Audio));
    }
}
