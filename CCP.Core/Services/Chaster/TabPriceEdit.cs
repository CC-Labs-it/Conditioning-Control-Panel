using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ConditioningControlPanel.Services.Chaster;

/// <summary>
/// The player's own figure for a price row (owner, 2026-09-29, tester feedback: "clicking on the
/// time lets you set it"). The table in <see cref="TabPrices"/> stays the default; an edit is a
/// machine-local override (<c>AppSettings.ChasterPriceOverrides</c>, never cloud-synced because
/// every Chaster* setting stays on the PC).
///
/// <para>Rules. The SIGN never changes: a cost stays a cost and an earn-back stays an earn-back.
/// The size is clamped to <see cref="MinSeconds"/>..<see cref="MaxSeconds"/> every time it is
/// read, so a hand-edited settings file cannot book past it, and every booking still goes
/// through the day and backlog limits after that. Only rows booked through
/// <see cref="TabPrices.Resolve"/> with one fixed figure take an edit: the way out never has a
/// figure, "misses" doubles by itself, the leash and a stake name their own size, and the
/// day-end rows are the service's own verdicts. Figures are set only while no lock runs; the
/// page enforces that with <see cref="CanEdit"/>.</para>
/// </summary>
public static class TabPriceEdit
{
    public const int MinSeconds = 5;

    /// <summary>A whole day (owner, 2026-11-05, WhoWho372: "skipping a session adds at least a
    /// day"). It was an hour. The day limit still decides what one day books.</summary>
    public const int MaxSeconds = 24 * 60 * 60;

    /// <summary>Past this, the page says the figure is a lot. The old ceiling.</summary>
    public const int WarnOverSeconds = 60 * 60;

    /// <summary>What the line under a row says about its figure: <c>Big</c> past an hour,
    /// <c>OverDay</c> when a cost is bigger than the day limit in force, so one event books at
    /// most the limit. An earn-back is never cut by the day limit (the floor holds it instead).</summary>
    public readonly record struct Warning(bool Big, bool OverDay)
    {
        public bool Any => Big || OverDay;
    }

    /// <summary>The warning for a signed figure against the day limit in force.</summary>
    public static Warning WarningFor(int seconds, int dailyLimitSeconds)
    {
        var magnitude = Math.Abs((long)seconds);
        return new(magnitude > WarnOverSeconds, seconds > 0 && seconds > dailyLimitSeconds);
    }

    /// <summary>The warning's words, one sentence each on one line. Empty for no warning.
    /// "{0}" in the day sentence is the day limit.</summary>
    public static string WarningText(Warning warning, int dailyLimitSeconds, Func<string, string?> loc)
    {
        if (!warning.Any) return "";
        var parts = new List<string>(2);
        if (warning.Big) parts.Add(loc("chaster_price_warn_big") ?? "");
        if (warning.OverDay)
            parts.Add((loc("chaster_price_warn_day") ?? "").Replace("{0}", CircesTab.Format(dailyLimitSeconds, signed: false)));
        return string.Join(" ", parts.Where(p => p.Length > 0));
    }

    /// <summary>The figure the row would book if the box's text were kept now: the typed one,
    /// clamped and signed like a commit, the current one when the text does not read as a time,
    /// and the default for empty text.</summary>
    public static int Preview(string id, string? text, IReadOnlyDictionary<string, int>? overrides) =>
        Parse(text) is { } seconds ? Effective(id, With(overrides, id, seconds)) : Effective(id, overrides);

    /// <summary>May this row's figure be set by the player at all: one fixed figure, never a
    /// way out, not a day-end verdict.</summary>
    public static bool Editable(string? id) =>
        TabMenuCopy.HasFixedFigure(id)
        && !TabPrices.NeverPriced.Contains(id!)
        && !TabDayEnd.ServiceRows.Contains(id!);

    /// <summary>Figures move only while no lock is running: not linked, or linked with no active
    /// lock. Chaster out of reach counts as a lock (it may well be one).</summary>
    public static bool CanEdit(bool linked, LockLookup lookup) => !linked || lookup == LockLookup.None;

    /// <summary>The signed seconds this row books for one event, with the player's figure when
    /// there is one. Unknown ids read 0.</summary>
    public static int Effective(string id, IReadOnlyDictionary<string, int>? overrides) =>
        TabPrices.Find(id) is { } price ? Effective(price, overrides) : 0;

    public static int Effective(TabPrice price, IReadOnlyDictionary<string, int>? overrides)
    {
        if (overrides == null || !Editable(price.Id) || !overrides.TryGetValue(price.Id, out var size)) return price.Seconds;
        var magnitude = Math.Clamp(Math.Abs((long)size), MinSeconds, MaxSeconds);
        return (int)(price.Seconds < 0 ? -magnitude : magnitude);
    }

    /// <summary>The row as the page shows it: the default record with the effective figure.</summary>
    public static TabPrice Shown(TabPrice price, IReadOnlyDictionary<string, int>? overrides) =>
        price with { Seconds = Effective(price, overrides) };

    public static bool IsEdited(string id, IReadOnlyDictionary<string, int>? overrides) =>
        TabPrices.Find(id) is { } price && Effective(price, overrides) != price.Seconds;

    /// <summary>What the player typed, as unsigned seconds. "2:30" and "1:05:00" are clock
    /// figures, a bare number is minutes ("5" is 5:00). Signs are ignored: the row keeps its own.
    /// Null for anything that does not read as a time (the edit is then dropped); empty text is
    /// 0, which <see cref="With"/> reads as "back to the default". Only empty text: a typed zero
    /// ("0", "0:00") asks for the row as small as it goes, <see cref="MinSeconds"/>.</summary>
    public static int? Parse(string? text)
    {
        var t = (text ?? "").Trim().TrimStart('+', '-', '−').Trim();
        if (t.Length == 0) return 0;
        var parts = t.Split(':');
        if (parts.Length > 3) return null;
        long total = 0;
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out var n)) return null;
            // Past the first part a field is a clock field: 0..59.
            if (i > 0 && (n > 59 || parts[i].Length != 2)) return null;
            total = total * 60 + n;
        }
        if (parts.Length == 1) total *= 60;
        if (total == 0) return MinSeconds;
        return total > int.MaxValue ? null : (int)total;
    }

    /// <summary>The overrides after setting one row. <paramref name="seconds"/> null or 0, or the
    /// default's own size, drops the row's entry (back to the table). A new map every time: the
    /// service reads the setting from any thread and must never see one being edited.</summary>
    public static Dictionary<string, int> With(IReadOnlyDictionary<string, int>? overrides, string id, int? seconds)
    {
        var next = new Dictionary<string, int>(StringComparer.Ordinal);
        if (overrides != null)
            foreach (var (k, v) in overrides)
                if (Editable(k)) next[k] = v;
        if (!Editable(id) || TabPrices.Find(id) is not { } price) return next;
        next.Remove(id);
        if (seconds is { } s && s != 0)
        {
            var magnitude = Math.Clamp(Math.Abs(s), MinSeconds, MaxSeconds);
            if (magnitude != Math.Abs(price.Seconds)) next[id] = magnitude;
        }
        return next;
    }
}
