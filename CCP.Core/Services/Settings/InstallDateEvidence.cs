using System;
using System.Globalization;
using System.IO;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services
{
    /// <summary>WPF App.xaml.cs:6237-6322: record the oldest plausible install evidence once.</summary>
    public static class InstallDateEvidence
    {
        public static bool Record(AppSettings settings, string userData, string appDirectory)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(settings.InstallDate)) return false;
                var probes = new System.Collections.Generic.List<Func<DateTime?>>();
                probes.Add(() => { var p = Path.Combine(userData, "settings.json"); return File.Exists(p) ? File.GetCreationTimeUtc(p) : null; });
                probes.Add(() => Directory.Exists(appDirectory) ? Directory.GetCreationTimeUtc(appDirectory) : null);
                try
                {
                    var logs = Path.Combine(userData, "logs");
                    if (Directory.Exists(logs))
                        foreach (var file in Directory.EnumerateFiles(logs))
                        {
                            var path = file;
                            probes.Add(() => File.GetCreationTimeUtc(path));
                        }
                }
                catch { }
                return Record(settings, DateTime.UtcNow, probes);
            }
            catch { return false; }
        }

        public static bool Record(AppSettings settings, DateTime now, System.Collections.Generic.IEnumerable<Func<DateTime?>> probes)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(settings.InstallDate)) return false;
                var today = now.Date;
                var floor = new DateTime(2020, 1, 1);
                DateTime? earliest = null;
                foreach (var probe in probes)
                {
                    try
                    {
                        var date = probe();
                        if (date >= floor && (earliest == null || date < earliest)) earliest = date;
                    }
                    catch { }
                }
                var result = (earliest ?? today).Date;
                if (result < floor) result = floor;
                if (result > today) result = today;
                settings.InstallDate = result.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                return true;
            }
            catch { return false; }
        }
    }
}
