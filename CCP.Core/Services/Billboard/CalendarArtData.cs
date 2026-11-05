using System;
using System.Collections.Generic;
namespace ConditioningControlPanel.Services.Billboard;
// Extracted from WPF Controls/Billboard/BuiltInArt.cs:80-115 at 74ac6ce3d.
public static class CalendarArtData
{
    private static int? Get(object? data, string key) =>
        data is IReadOnlyDictionary<string, int> d && d.TryGetValue(key, out var value) ? value : null;
    /// <summary>Sparse raffle dates, program days, legacy counts or the current month, clamped as WPF.</summary>
    public static BillboardCalendarInfo Parse(object? data, DateTime? nowLocal = null)
    {
        var now = nowLocal ?? DateTime.Now;
        int monthDays = DateTime.DaysInMonth(now.Year, now.Month);
        var (counted, lit, needed, days, today) = data switch
        {
            int[] a when a.Length >= 2 => (a[0], (int?)null, a[1], monthDays, now.Day),
            _ when Get(data, "day") is int day && Get(data, "counted") == null => Program(day, Get(data, "days") ?? Math.Max(day, monthDays)),
            IReadOnlyDictionary<string, int> => (Get(data, "counted") ?? 0, Get(data, "mask"), Get(data, "need") ?? 0, Get(data, "days") ?? monthDays, Get(data, "today") ?? now.Day),
            _ => (0, (int?)null, 0, monthDays, now.Day),
        };
        days = Math.Clamp(days, 1, 31);
        counted = Math.Clamp(counted, 0, days);
        int mask = (lit ?? (int)((1L << counted) - 1)) & (int)((1L << days) - 1);
        return new BillboardCalendarInfo(counted, Math.Clamp(needed, 0, 31), days, Math.Clamp(today, 1, days), mask);
    }
    private static (int Counted, int? Lit, int Needed, int Days, int Today) Program(int day, int days) => (day - 1, null, days, days, day);
}
/// <summary>Lit is a mask of real dates (bit 0 = day 1); Counted drives progress toward Needed.</summary>
public readonly record struct BillboardCalendarInfo(int Counted, int Needed, int Days, int Today, int Lit)
{
    public bool IsLit(int day) => day >= 1 && day <= 31 && (Lit & (1 << (day - 1))) != 0;
}
