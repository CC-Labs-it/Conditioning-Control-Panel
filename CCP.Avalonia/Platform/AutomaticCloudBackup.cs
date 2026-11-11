using System;
using System.Threading.Tasks;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>WPF App.xaml.cs:693: the settings save hook shares the manual backup gate.</summary>
internal static class AutomaticCloudBackup
{
    internal static CloudSettingsBackup Client = new();
    private static readonly object Gate = new();
    private static Task? _pending;

    internal static void Seed() => CoreSettingsHooks.CloudBackup = Start;

    private static Task Start()
    {
        lock (Gate)
        {
            if (_pending is { IsCompleted: false }) return _pending;
            // Run off the dispatcher: shutdown waits synchronously, while HTTP and recovery await.
            return _pending = Task.Run(async () =>
            {
                var service = CoreSettings.Service;
                if (service != null)
                    await Client.BackupAsync(service.Current, force: false, restoredFromBackup: service.RestoredFromBackup);
            });
        }
    }

    internal static bool WaitForExit(TimeSpan? timeout = null)
    {
        try { return Start().Wait(timeout ?? TimeSpan.FromSeconds(2)); }
        catch (Exception ex) { Serilog.Log.Debug(ex, "Exit settings backup failed"); return false; }
    }
}
