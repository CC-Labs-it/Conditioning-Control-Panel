using System;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Services.JustDrop
{
    /// <summary>WPF MainWindow.Takeaway: shared receipt text, never a guessed duration.</summary>
    internal static class TakeawayText
    {
        internal static string Name(JustDropOrdersService.Order order) =>
            string.IsNullOrWhiteSpace(order.Name) ? Loc.Get("sd_takeaway_order_fallback") : order.Name;
        internal static string Date(JustDropOrdersService.Order order) => order.At.ToString("MMM d").ToUpperInvariant();
        internal static string Meta(JustDropOrdersService.Order order) => order.Minutes > 0
            ? Loc.GetF("sd_takeaway_meta", order.Minutes, Date(order)) : Date(order);
        internal static string Minutes(JustDropOrdersService.Order order) => order.Minutes > 0
            ? string.Format(Or("takeaway_row_min", "{0} MIN"), order.Minutes) : "";
        internal static string Age(JustDropOrdersService.Order order, DateTimeOffset now)
        {
            int days = (int)Math.Floor((now - order.At).TotalDays);
            return days <= 0 ? Or("takeaway_today", "today") : string.Format(
                days == 1 ? Or("takeaway_day_ago", "{0} day ago") : Or("takeaway_days_ago", "{0} days ago"), days);
        }
        internal static string More(int count, bool open) =>
            string.Format(Or("sd_takeaway_more", "+{0} more"), count) + (open ? "  ▴" : "  ▾");
        private static string Or(string key, string fallback)
        {
            var text = Loc.Get(key);
            return string.IsNullOrEmpty(text) || text == key ? fallback : text;
        }
    }
}
