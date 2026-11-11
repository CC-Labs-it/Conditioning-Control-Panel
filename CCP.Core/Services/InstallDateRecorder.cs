using System;
using System.Globalization;
using System.IO;

namespace ConditioningControlPanel.Services
{
    public static class InstallDateRecorder
    {
        private static readonly DateTime Floor = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public static void EnsureRecorded(SettingsService settings, string userData, string executableDirectory)
        {
            if (!string.IsNullOrWhiteSpace(settings.Current.InstallDate)) return;
            settings.Current.InstallDate = Resolve(userData, executableDirectory).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            settings.Save();
        }

        internal static DateTime Resolve(string userData, string executableDirectory)
        {
            var today = DateTime.UtcNow.Date;
            DateTime? earliest = null;
            void Consider(DateTime value)
            {
                if (value >= Floor && (earliest == null || value < earliest)) earliest = value;
            }
            try { var path = Path.Combine(userData, "settings.json"); if (File.Exists(path)) Consider(File.GetCreationTimeUtc(path)); } catch { }
            try { if (Directory.Exists(executableDirectory)) Consider(Directory.GetCreationTimeUtc(executableDirectory)); } catch { }
            try
            {
                var logs = Path.Combine(userData, "logs");
                if (Directory.Exists(logs))
                    foreach (var file in Directory.EnumerateFiles(logs))
                        try { Consider(File.GetCreationTimeUtc(file)); } catch { }
            }
            catch { }
            var result = (earliest ?? today).Date;
            return result > today ? today : result < Floor ? Floor : result;
        }
    }
}
