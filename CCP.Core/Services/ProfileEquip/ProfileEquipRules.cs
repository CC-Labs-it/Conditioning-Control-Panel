using System;
using System.Globalization;
using ConditioningControlPanel.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.ProfileEquip;

/// <summary>The two Trainer Card slots the equip route owns. Null = nothing worn.</summary>
public readonly record struct ProfileSlots(string? FrameId, string? AvatarId)
{
    public bool HasAny => FrameId != null || AvatarId != null;

    public static ProfileSlots Of(ProfileCosmetics? c) => new(Clean(c?.FrameId), Clean(c?.AvatarId));

    internal static string? Clean(string? id)
    {
        var s = id?.Trim();
        return string.IsNullOrEmpty(s) ? null : s;
    }
}

/// <summary>What a pull does once the server has answered.</summary>
public enum ProfileEquipStep { None, Push, Adopt }

/// <summary>Shared profile equip protocol and settlement decisions (WPF Services/Profile/ProfileEquipSync.cs, pure half).
/// Own namespace: the WPF tree still defines ProfileSlots/ProfileEquipStep in ConditioningControlPanel.Services.</summary>
public static class ProfileEquipRules
{
    public const string Family = "profile";
    private static readonly JsonSerializerSettings NoDates = new() { DateParseHandling = DateParseHandling.None };

    /// <summary>
    /// The PUT body. A pick sends both slots, since a null clears and a missing slot keeps the server's value.
    /// The first-run push leaves its empty slots out (<paramref name="keepEmpty"/>): an empty slot on this PC
    /// is not a decision to clear what was picked elsewhere. Pure.
    /// </summary>
    public static JObject BuildPut(string unifiedId, ProfileSlots slots, bool keepEmpty = false)
    {
        var value = new JObject();
        if (slots.FrameId != null || !keepEmpty) value["frame_id"] = OrNull(slots.FrameId);
        if (slots.AvatarId != null || !keepEmpty) value["avatar_id"] = OrNull(slots.AvatarId);
        return new JObject { ["unified_id"] = unifiedId, ["family"] = Family, ["value"] = value };
    }

    /// <summary>
    /// True for an ok reply that came from the equip route at all. An older server answers with no
    /// <c>equip.profile</c>; that is still an answer, unlike a refusal. Pure, never throws.
    /// </summary>
    public static bool IsEquipReply(JObject? reply)
    {
        try { return reply?.Value<bool?>("ok") == true && reply["equip"] is JObject; }
        catch { return false; }
    }

    /// <summary>The profile copy and its stamp out of a reply, or null when it carries none. Pure, never throws.</summary>
    public static (ProfileSlots Slots, long UpdatedMs)? ReadProfile(JObject? reply)
    {
        try
        {
            if (!IsEquipReply(reply) || reply!["equip"]!["profile"] is not JObject p) return null;
            return (new ProfileSlots(Str(p["frame_id"]), Str(p["avatar_id"])), Stamp(reply["equip"]!["updatedAt"]));
        }
        catch { return null; }
    }

    /// <summary>True when a server copy stamped <paramref name="serverMs"/> is newer than the local one.</summary>
    public static bool ShouldAdopt(long serverMs, long localMs) => serverMs > 0 && serverMs > localMs;

    /// <summary>
    /// The whole decision. <paramref name="storedAccount"/> is who the stored stamp and push belong to: null
    /// = this client version has never settled a pick here. <paramref name="server"/> null = the route answered
    /// with no profile copy. Pure.
    /// </summary>
    public static ProfileEquipStep Decide(string? storedAccount, string account, bool pushOwed,
        ProfileSlots local, ProfileSlots? server, long serverMs, long localMs)
    {
        // Somebody else's stamp and pending pick mean nothing here, and the loadout on this PC is theirs
        // too: never push it. A pick the signed-in account already has still lands.
        if (storedAccount != null && storedAccount != account)
            return server is { HasAny: true } theirs && theirs != local ? ProfileEquipStep.Adopt : ProfileEquipStep.None;

        if (pushOwed) return ProfileEquipStep.Push;

        // Push once before the first adoption: neither the shared stamp nor the mirrored slots can say
        // whether the server's copy is a pick. With nothing here there is nothing to lose.
        if (storedAccount == null)
            return local.HasAny ? ProfileEquipStep.Push
                : server is { HasAny: true } ? ProfileEquipStep.Adopt : ProfileEquipStep.None;

        // Another family's PUT moves the stamp too: equal slots are nothing to adopt.
        return server is { } copy && copy != local && ShouldAdopt(serverMs, localMs)
            ? ProfileEquipStep.Adopt : ProfileEquipStep.None;
    }

    /// <summary>A copy of <paramref name="current"/> with the two slots changed and nothing else. Pure.</summary>
    public static ProfileCosmetics WithSlots(ProfileCosmetics? current, ProfileSlots slots)
    {
        var next = (current ?? new ProfileCosmetics()).Clone();
        next.FrameId = slots.FrameId;
        next.AvatarId = slots.AvatarId;
        return next;
    }

    public static long MarkOwed(AppSettings s)
    {
        var stamp = Math.Max(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), s.ProfileEquipUpdatedMs + 1);
        s.ProfileEquipUpdatedMs = stamp;
        s.ProfileEquipPushOwed = true;
        return stamp;
    }

    public static JObject? ParseObject(string body)
    {
        try { return JsonConvert.DeserializeObject(body, NoDates) as JObject; }
        catch { return null; }
    }

    private static JToken OrNull(string? id) => id == null ? JValue.CreateNull() : new JValue(id);

    private static string? Str(JToken? t) => t is JValue { Type: JTokenType.String } v ? ProfileSlots.Clean((string?)v) : null;

    private static long Stamp(JToken? t)
    {
        if (t is JValue { Type: JTokenType.Integer or JTokenType.Float } n) return (long)n.Value<double>();
        if (t is JValue { Type: JTokenType.Date, Value: DateTime dt })
            return new DateTimeOffset(dt.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(dt, DateTimeKind.Utc) : dt.ToUniversalTime())
                .ToUnixTimeMilliseconds();
        if (t is JValue { Type: JTokenType.String } s
            && DateTimeOffset.TryParse((string?)s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at))
            return at.ToUnixTimeMilliseconds();
        return 0;
    }
}
