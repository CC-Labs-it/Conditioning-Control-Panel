using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using ConditioningControlPanel.Services.Companion.Brain;
using ConditioningControlPanel.Services.Moderation;

namespace ConditioningControlPanel.Services.Companion;

/// <summary>
/// The pure rules of the v2 conversation page, shared by both heads: WPF ConversationPageVm,
/// PreferredNameEditor and ConversationRecap (Views/Controls/Companion/V2) call these.
/// </summary>
internal static class ConversationRules
{
    internal enum NameSave { Saved, Refused, Stale }

    /// <summary>Collapses whitespace and caps at 64 text elements (WPF PreferredNameEditor.Normalize).</summary>
    internal static string NormalizePreferredName(string? input)
    {
        var clean = Regex.Replace(input ?? string.Empty, @"\s+", " ").Trim();
        var elements = StringInfo.ParseCombiningCharacters(clean);
        return elements.Length > 64 ? clean[..elements[64]] : clean;
    }

    /// <summary>
    /// WPF PreferredNameEditor.Save: a store that changed account since the editor opened is stale and
    /// writes nothing; a name the moderation gate refuses is not stored; an empty name clears it.
    /// </summary>
    internal static (NameSave Result, string Name) SavePreferredName(IMemoryStore? current, IMemoryStore? owner,
        string? input, IModerationGuard? guard)
    {
        if (current == null || !ReferenceEquals(current, owner)) return (NameSave.Stale, string.Empty);
        var name = NormalizePreferredName(input);
        if (!MemoryStore.IsStorable(guard, name, MemoryFact.SourceUserEdited)) return (NameSave.Refused, string.Empty);
        current.UpdateProfileSignal(MemoryStore.KeyPreferredName, name.Length == 0 ? null : name);
        return (NameSave.Saved, name);
    }

    internal static string PreferredName(IMemoryStore? store) =>
        store?.Profile.TryGetValue(MemoryStore.KeyPreferredName, out var value) == true ? value?.ToString() ?? string.Empty : string.Empty;

    /// <summary>The rolling summary a later message can carry (WPF ConversationRecap); empty hides the recap.</summary>
    internal static IReadOnlyList<string> Recap(CompanionBrain? brain)
    {
        brain?.EnsureCurrentAccount();
        return brain?.SummaryQuotes ?? Array.Empty<string>();
    }

    /// <summary>Clears the summary only for the account the recap was built for (WPF ConversationRecap clear).</summary>
    internal static bool ClearRecap(CompanionBrain? brain, IMemoryStore? owner)
    {
        brain?.EnsureCurrentAccount();
        if (brain == null || !ReferenceEquals(owner, brain.Memory)) return false;
        brain.ClearSummary();
        return true;
    }

    // Every AI line the tube said shows here too (#1443): ambient replies and display-only tube lines.
    internal static bool Shows(TurnKind kind) =>
        kind is TurnKind.UserChat or TurnKind.AssistantChat or TurnKind.AmbientReply or TurnKind.TubeLine;

    internal static bool ShouldSpeak(string? text, bool avatarEnabled) => avatarEnabled && !string.IsNullOrWhiteSpace(text);

    internal static string FailureNoticeKey(AiFailureKind? failure) => failure switch
    {
        AiFailureKind.SignInRequired => "companion_v2_error_signin",
        AiFailureKind.DailyLimit => "companion_v2_error_limit",
        AiFailureKind.BudgetLimit => "companion_v2_error_budget",
        AiFailureKind.Offline => "companion_v2_error_offline",
        AiFailureKind.Busy => "companion_v2_busy",
        AiFailureKind.Cancelled => "companion_v2_cancelled",
        AiFailureKind.InvalidResponse => "companion_v2_error_invalid",
        _ => "companion_v2_unavailable"
    };
}
