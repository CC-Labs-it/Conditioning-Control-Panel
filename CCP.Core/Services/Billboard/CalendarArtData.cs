using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services.Billboard;

// WPF ConditioningControlPanel/Controls/Billboard/BuiltInArt.cs:80-115.
public static class CalendarArtData
{
    private static int? Get(object? data, string key) =>
        data is IReadOnlyDictionary<string, int> d && d.TryGetValue(key, out var value) ? value : null;

    /// <summary>WPF calendar payloads: sparse raffle dates, program days, legacy counts or the current month.</summary>
    public static BillboardCalendarInfo Parse(object? data, DateTime? nowLocal = null)
    {
        var now = nowLocal ?? DateTime.Now;
        int monthDays = DateTime.DaysInMonth(now.Year, now.Month);
        int counted = 0, needed = 0, days = monthDays, today = now.Day;
        int? lit = null;
        if (data is int[] a && a.Length >= 2) { counted = a[0]; needed = a[1]; }
        else if (Get(data, "day") is int day && Get(data, "counted") == null)
        {
            today = day;
            days = Get(data, "days") ?? Math.Max(day, monthDays);
            counted = day - 1;
            needed = days;
        }
        else if (data is IReadOnlyDictionary<string, int>)
        {
            counted = Get(data, "counted") ?? 0;
            lit = Get(data, "mask");
            needed = Get(data, "need") ?? 0;
            days = Get(data, "days") ?? monthDays;
            today = Get(data, "today") ?? now.Day;
        }
        days = Math.Clamp(days, 1, 31);
        counted = Math.Clamp(counted, 0, days);
        int inMonth = (int)((1L << days) - 1);
        int mask = (lit ?? (int)((1L << counted) - 1)) & inMonth;
        return new BillboardCalendarInfo(counted, Math.Clamp(needed, 0, 31), days, Math.Clamp(today, 1, days), mask);
    }
}

/// <summary>What the calendar draws: the <see cref="Lit"/> days filled (bit 0 = day 1, the
/// real dates), the bar from <see cref="Counted"/> toward needed, today ringed.</summary>
public readonly record struct BillboardCalendarInfo(int Counted, int Needed, int Days, int Today, int Lit)
{
    public bool IsLit(int day) => day >= 1 && day <= 31 && (Lit & (1 << (day - 1))) != 0;
}
