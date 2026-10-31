using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Companion
{
    /// <summary>How one wardrobe item can be worn (RAIN-V2-PLAN 2.5, items.json <c>access</c>).</summary>
    public enum RainItemAccessKind { Free, Unlock, Shop }

    /// <summary>
    /// One row of <c>Resources/web/rain-wardrobe/items.json</c>: the only bridge between the fitting room's
    /// page ids and the Prize Counter's catalog ids. <see cref="Id"/> is <c>slot:pageId</c>, because page ids
    /// repeat across slots (arms and ankles both have <c>fine-gold</c>).
    /// </summary>
    public sealed record RainItem(
        string Id, string Slot, string PageId, RainItemAccessKind Access,
        string? Grant, string? Tab, int? UnlockLevel, string? UnlockAchievement)
    {
        public static string KeyOf(string slot, string pageId) => slot + ":" + pageId;
    }

    /// <summary>The parsed items.json. PURE apart from <see cref="Load"/>.</summary>
    public sealed class RainItemManifest
    {
        public const int Rev = 1;
        public const string FileName = "items.json";

        /// <summary>An achievement unlock of <c>"any"</c> opens on the account's first achievement.</summary>
        public const string AnyAchievement = "any";

        public static readonly RainItemManifest Empty = new(Array.Empty<RainItem>());

        private static readonly Lazy<RainItemManifest> _shipped = new(() => Load());

        /// <summary>The shipped items.json, read once.</summary>
        public static RainItemManifest Shipped => _shipped.Value;

        private readonly Dictionary<string, RainItem> _byKey;

        public IReadOnlyList<RainItem> Items { get; }

        private RainItemManifest(IReadOnlyList<RainItem> items)
        {
            Items = items;
            _byKey = new Dictionary<string, RainItem>(StringComparer.Ordinal);
            foreach (var i in items) _byKey[i.Id] = i;
        }

        /// <summary>The item for (<paramref name="slot"/>, <paramref name="pageId"/>), or null when the manifest does not know it.</summary>
        public RainItem? Find(string? slot, string? pageId)
            => slot == null || pageId == null ? null : _byKey.TryGetValue(RainItem.KeyOf(slot, pageId), out var i) ? i : null;

        /// <summary>Parses items.json; malformed rows are skipped, anything unreadable or another rev is <see cref="Empty"/>.</summary>
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
                RainItemAccessKind kind;
                switch ((string?)row["access"])
                {
                    case "free": kind = RainItemAccessKind.Free; break;
                    case "unlock": kind = RainItemAccessKind.Unlock; break;
                    case "shop": kind = RainItemAccessKind.Shop; break;
                    default: continue;
                }
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
                items.Add(new RainItem(RainItem.KeyOf(slot, pageId), slot, pageId, kind, grant, (string?)row["tab"], level, achievement));
            }
            return new RainItemManifest(items);
        }

        /// <summary>The shipped manifest next to the exe; <see cref="Empty"/> when it is missing or unreadable.</summary>
        public static RainItemManifest Load(string? path = null)
        {
            path ??= Path.Combine(AppContext.BaseDirectory, "Resources", "web", "rain-wardrobe", FileName);
            try { return File.Exists(path) ? Parse(File.ReadAllText(path)) : Empty; }
            catch (Exception) { return Empty; }
        }
    }

    /// <summary>The account's grants (the prizes block of /v2/user/sync).</summary>
    public interface IRainGrantSource { bool IsGranted(string grant); }

    /// <summary>The account's level and unlocked achievement ids.</summary>
    public interface IRainProgressSource
    {
        int Level { get; }
        IReadOnlyCollection<string> UnlockedAchievements { get; }
    }

    /// <summary>A fixed grant list (the wardrobe window passes <c>App.Ownership?.Grants</c>; null = none).</summary>
    public sealed class RainGrantSet : IRainGrantSource
    {
        private readonly HashSet<string> _grants;
        public RainGrantSet(IEnumerable<string>? grants) => _grants = new HashSet<string>(grants ?? Array.Empty<string>(), StringComparer.Ordinal);
        public bool IsGranted(string grant) => _grants.Contains(grant);
    }

    /// <summary>The live app: <see cref="Prizes.PrizeGrants.IsGranted"/>.</summary>
    public sealed class AppRainGrantSource : IRainGrantSource
    {
        public bool IsGranted(string grant) => Prizes.PrizeGrants.IsGranted(grant);
    }

    /// <summary>
    /// The live app: the higher of the current and the highest-ever level (a re-priced curve never takes an
    /// earned piece back off Rain) and <c>App.Achievements</c>.
    /// </summary>
    public sealed class AppRainProgressSource : IRainProgressSource
    {
        public int Level
        {
            get
            {
                var s = App.Settings?.Current;
                return s == null ? 0 : Math.Max(s.PlayerLevel, s.HighestLevelEver);
            }
        }

        public IReadOnlyCollection<string> UnlockedAchievements
            => (IReadOnlyCollection<string>?)App.Achievements?.Progress?.UnlockedAchievements?.ToArray() ?? Array.Empty<string>();
    }

    /// <summary>What one item is for this account right now.</summary>
    public enum RainItemState
    {
        /// <summary>Not in the manifest: never wearable.</summary>
        Unknown,
        Free,
        /// <summary>A shop item the account's grants cover.</summary>
        Owned,
        /// <summary>A level or achievement item the account has earned.</summary>
        Unlocked,
        /// <summary>A level or achievement item not earned yet (see <see cref="RainItemStatus.NeedsLevel"/> / <see cref="RainItemStatus.NeedsAchievement"/>).</summary>
        Locked,
        /// <summary>A shop item the account does not own.</summary>
        Buyable,
    }

    public readonly record struct RainItemStatus(RainItemState State, RainItem? Item, int? NeedsLevel = null, string? NeedsAchievement = null)
    {
        public bool Wearable => State is RainItemState.Free or RainItemState.Owned or RainItemState.Unlocked;
    }

    /// <summary>
    /// The state of each of Rain's wardrobe items for the current account (RAIN-V2-PLAN 2.4: unlocks are
    /// evaluated client side, cosmetic, tube only; shop items follow the server's <c>cos.*</c> grants).
    /// PURE given its sources.
    /// </summary>
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

        /// <summary>The live app's view: the shipped manifest, PrizeGrants, level and achievements.</summary>
        public static RainItemAccess ForApp()
            => new(RainItemManifest.Shipped, new AppRainGrantSource(), new AppRainProgressSource());

        public RainItemManifest Manifest => _manifest;

        public RainItemStatus StateOf(string? slot, string? pageId) => StateOf(_manifest.Find(slot, pageId));

        public RainItemStatus StateOf(RainItem? item)
        {
            if (item == null) return new RainItemStatus(RainItemState.Unknown, null);
            switch (item.Access)
            {
                case RainItemAccessKind.Free:
                    return new RainItemStatus(RainItemState.Free, item);
                case RainItemAccessKind.Shop:
                    return new RainItemStatus(item.Grant != null && _grants.IsGranted(item.Grant)
                        ? RainItemState.Owned : RainItemState.Buyable, item);
                default:
                    bool levelOk = item.UnlockLevel is not int need || _progress.Level >= need;
                    bool achievementOk = string.IsNullOrWhiteSpace(item.UnlockAchievement) || HasAchievement(item.UnlockAchievement!);
                    if (levelOk && achievementOk) return new RainItemStatus(RainItemState.Unlocked, item);
                    return new RainItemStatus(RainItemState.Locked, item,
                        levelOk ? null : item.UnlockLevel,
                        achievementOk ? null : item.UnlockAchievement);
            }
        }

        public bool IsWearable(string? slot, string? pageId) => StateOf(slot, pageId).Wearable;

        private bool HasAchievement(string id)
        {
            var unlocked = _progress.UnlockedAchievements;
            if (unlocked == null) return false;
            return id == RainItemManifest.AnyAchievement ? unlocked.Count > 0 : unlocked.Contains(id);
        }
    }
}
