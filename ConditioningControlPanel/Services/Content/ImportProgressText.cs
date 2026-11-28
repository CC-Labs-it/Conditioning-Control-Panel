using System;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// What the drop overlay shows while a dropped pack unpacks (ccp-bugs #1706).
    /// Before the first report the total is unknown, so the bar runs indeterminate.
    /// </summary>
    public static class ImportProgressText
    {
        public const int MaxFileChars = 40;

        public static bool IsIndeterminate(ImportProgress? p) => p == null || p.Total <= 0;

        /// <summary>0..100, clamped; 0 while the total is unknown.</summary>
        public static double Percent(ImportProgress? p)
        {
            if (IsIndeterminate(p)) return 0;
            var current = Math.Clamp(p!.Current, 0, p.Total);
            return (double)current / p.Total * 100.0;
        }

        /// <summary>A long file name keeps its start and its extension.</summary>
        public static string ShortFileName(string? name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            if (name.Length <= MaxFileChars) return name;
            var ext = System.IO.Path.GetExtension(name);
            if (ext.Length > 8) ext = "";
            var keep = MaxFileChars - ext.Length - 3;
            return name.Substring(0, keep) + "..." + ext;
        }

        /// <summary>The count line: "37 / 412 - file.mp4", localised by the caller's format.</summary>
        public static string CountLine(ImportProgress? p, Func<int, int, string, string> format)
        {
            if (IsIndeterminate(p)) return "";
            var current = Math.Clamp(p!.Current, 0, p.Total);
            return format(current, p.Total, ShortFileName(p.CurrentFile));
        }
    }
}
