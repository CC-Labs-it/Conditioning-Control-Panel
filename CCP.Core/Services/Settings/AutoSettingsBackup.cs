using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;
using Serilog;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// The automatic cloud settings backup for a head that is not WPF: WPF ProfileSyncService.BackupSettingsAsync
    /// with force: false (ProfileSyncService.cs:4317-4320, 4464-4510, 4582-4588), seeded as
    /// <see cref="CoreSettingsHooks.CloudBackup"/> so every settings save (and the exit flush) offers one, as WPF
    /// App.xaml.cs:696-697 does. SettingsService keeps its own 30 s attempt gate; this adds WPF's rules:
    /// <list type="bullet">
    /// <item>at most one upload per five minutes, the slot claimed before the request so concurrent saves never send two;</item>
    /// <item>never while the settings file came from a rolling daily backup (#761: it could overwrite the one
    /// cloud copy that still holds the pre-corruption state);</item>
    /// <item>a 413 stops automatic backups for the session (the same body would be refused again).</item>
    /// </list>
    /// The upload itself is <see cref="CloudSettingsBackup.BackupAsync"/>: paths stay on this machine, the
    /// slot's own path fields ride along, 409 and 401 go to the shared recovery. "Backup now" stays forced
    /// and bypasses all three rules, as WPF.
    /// </summary>
    public sealed class AutoSettingsBackup
    {
        public static readonly TimeSpan Debounce = TimeSpan.FromMinutes(5);

        private readonly Func<CloudSettingsBackup> _client;
        private long _lastTicks;
        private int _refusedTooLarge;

        /// <summary>Tests only: the clock the debounce reads.</summary>
        public Func<DateTime> UtcNow { get; set; } = () => DateTime.UtcNow;

        /// <param name="client">Builds the backup client per attempt (tests pass one on a fake handler).</param>
        public AutoSettingsBackup(Func<CloudSettingsBackup>? client = null) =>
            _client = client ?? (() => new CloudSettingsBackup());

        /// <summary>WPF force: true stamps the time as well, so a "Backup now" restarts the five minutes.</summary>
        public void NoteForced() => Interlocked.Exchange(ref _lastTicks, UtcNow().Ticks);

        /// <summary>True when an upload went out and the server took it.</summary>
        public async Task<bool> RunAsync(AppSettings? settings, bool restoredFromBackup)
        {
            if (settings == null || settings.OfflineMode || string.IsNullOrEmpty(settings.UnifiedId)) return false;
            if (restoredFromBackup)
            {
                Log.Debug("Settings cloud backup skipped - local settings came from a daily backup and have not been reconciled yet");
                return false;
            }
            if (Volatile.Read(ref _refusedTooLarge) == 1)
            {
                Log.Debug("Settings backup skipped (server refused the last one as too large)");
                return false;
            }
            var now = UtcNow().Ticks;
            var last = Interlocked.Read(ref _lastTicks);
            if (now - last < Debounce.Ticks)
            {
                Log.Debug("Settings backup skipped (debounce, last backup {Ago}s ago)", (now - last) / TimeSpan.TicksPerSecond);
                return false;
            }
            // Claim the slot BEFORE the request: a retry storm or a second save cannot send another.
            if (Interlocked.CompareExchange(ref _lastTicks, now, last) != last) return false;

            var (ok, refused) = await _client().BackupWithStatusAsync(settings);
            if (refused == HttpStatusCode.RequestEntityTooLarge)
            {
                Interlocked.Exchange(ref _refusedTooLarge, 1);
                Log.Warning("Settings backup refused as too large (413); no more automatic backups this session");
            }
            return ok;
        }
    }
}
