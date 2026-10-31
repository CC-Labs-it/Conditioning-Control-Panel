using System; using System.Collections.Generic; using System.IO; using System.Linq; using Newtonsoft.Json; using Newtonsoft.Json.Linq;
namespace ConditioningControlPanel.Services.Companion.Rain;
public enum RainItemAccessKind { Free, Unlock, Shop }
public sealed record RainItem(
    string Id, string Slot, string PageId, RainItemAccessKind Access,
    string? Grant, string? Tab, int? UnlockLevel, string? UnlockAchievement)
{ public static string KeyOf(string slot, string pageId) => slot + ":" + pageId; }
public sealed class RainItemManifest
{
    public const int Rev = 1;
    public const string FileName = "items.json";
    public const string AnyAchievement = "any";
    public static readonly RainItemManifest Empty = new(Array.Empty<RainItem>());
    private static readonly Lazy<RainItemManifest> _shipped = new(() => Load());
    public static RainItemManifest Shipped => _shipped.Value;
    private readonly Dictionary<string, RainItem> _byKey;
    public IReadOnlyList<RainItem> Items { get; }
    private RainItemManifest(IReadOnlyList<RainItem> items)
    {
        Items = items;
        _byKey = new Dictionary<string, RainItem>(StringComparer.Ordinal);
        foreach (var i in items) _byKey[i.Id] = i;
    }
    public RainItem? Find(string? slot, string? pageId)
        => slot == null || pageId == null ? null : _byKey.TryGetValue(RainItem.KeyOf(slot, pageId), out var i) ? i : null;
    public static RainItemManifest Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Empty;
        JObject root;
        try { root = JObject.Parse(json); }
        catch (JsonException) { return Empty; }
        if (root["rev"] is not JValue { Type: JTokenType.Integer } rev || rev.Value<long>() != Rev) return Empty;
        if (root["items"] is not JArray rows) return Empty;
        var items = new List<RainItem>();
        foreach (var row in rows.OfType<JObject>())
        {
            var slot = (string?)row["slot"];
            var pageId = (string?)row["pageId"];
            if (string.IsNullOrWhiteSpace(slot) || string.IsNullOrWhiteSpace(pageId)) continue;
            var kind = (string?)row["access"] switch { "free" => RainItemAccessKind.Free,
                "unlock" => RainItemAccessKind.Unlock, "shop" => RainItemAccessKind.Shop, _ => (RainItemAccessKind?)null };
            if (kind == null) continue;
            var grant = (string?)row["grant"];
            if (kind == RainItemAccessKind.Shop && string.IsNullOrWhiteSpace(grant)) continue;
            int? level = null;
            string? achievement = null;
            if (row["unlock"] is JObject unlock)
            {
                if (unlock["level"] is JValue { Type: JTokenType.Integer } lv) level = lv.Value<int>();
                achievement = (string?)unlock["achievement"];
            }
            if (kind == RainItemAccessKind.Unlock && level == null && string.IsNullOrWhiteSpace(achievement)) continue;
            items.Add(new RainItem(RainItem.KeyOf(slot, pageId), slot, pageId, kind.Value, grant, (string?)row["tab"], level, achievement));
        }
        return new RainItemManifest(items);
    }
    public static RainItemManifest Load(string? path = null)
    {
        path ??= Path.Combine(AppContext.BaseDirectory, "Resources", "web", "rain-wardrobe", FileName);
        try { return File.Exists(path) ? Parse(File.ReadAllText(path)) : Empty; }
        catch (Exception) { return Empty; }
    }
}
public interface IRainGrantSource { bool IsGranted(string grant); }
public interface IRainProgressSource { int Level { get; } IReadOnlyCollection<string> UnlockedAchievements { get; } }
public sealed class RainGrantSet : IRainGrantSource
{
    private readonly HashSet<string> _grants;
    public RainGrantSet(IEnumerable<string>? grants) => _grants = new HashSet<string>(grants ?? Array.Empty<string>(), StringComparer.Ordinal);
    public bool IsGranted(string grant) => _grants.Contains(grant);
}
public enum RainItemState { Unknown, Free, Owned, Unlocked, Locked, Buyable }
public readonly record struct RainItemStatus(RainItemState State, RainItem? Item, int? NeedsLevel = null, string? NeedsAchievement = null)
{ public bool Wearable => State is RainItemState.Free or RainItemState.Owned or RainItemState.Unlocked; }
public sealed class RainItemAccess
{
    private readonly RainItemManifest _manifest;
    private readonly IRainGrantSource _grants;
    private readonly IRainProgressSource _progress;
    public RainItemAccess(RainItemManifest manifest, IRainGrantSource grants, IRainProgressSource progress)
    {
        _manifest = manifest ?? RainItemManifest.Empty;
        _grants = grants ?? throw new ArgumentNullException(nameof(grants));
        _progress = progress ?? throw new ArgumentNullException(nameof(progress));
    }
    public RainItemManifest Manifest => _manifest;
    public RainItemStatus StateOf(string? slot, string? pageId) => StateOf(_manifest.Find(slot, pageId));
    public RainItemStatus StateOf(RainItem? item)
    {
        if (item == null) return new RainItemStatus(RainItemState.Unknown, null);
        if (item.Access == RainItemAccessKind.Free) return new(RainItemState.Free, item);
        if (item.Access == RainItemAccessKind.Shop) return new(item.Grant != null && _grants.IsGranted(item.Grant)
            ? RainItemState.Owned : RainItemState.Buyable, item);
        bool levelOk = item.UnlockLevel is not int need || _progress.Level >= need;
        bool achievementOk = string.IsNullOrWhiteSpace(item.UnlockAchievement) || HasAchievement(item.UnlockAchievement!);
        return levelOk && achievementOk ? new(RainItemState.Unlocked, item)
            : new(RainItemState.Locked, item, levelOk ? null : item.UnlockLevel, achievementOk ? null : item.UnlockAchievement);
    }

    public bool IsWearable(string? slot, string? pageId) => StateOf(slot, pageId).Wearable;
    private bool HasAchievement(string id)
    {
        var unlocked = _progress.UnlockedAchievements;
        if (unlocked == null) return false;
        return id == RainItemManifest.AnyAchievement ? unlocked.Count > 0 : unlocked.Contains(id);
    }
}
