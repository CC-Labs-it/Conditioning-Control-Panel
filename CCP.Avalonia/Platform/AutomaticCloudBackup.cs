using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>WPF App.xaml.cs:693: the settings save hook shares the manual backup gate.</summary>
internal static class AutomaticCloudBackup
{
    internal static CloudSettingsBackup Client = new();

    internal static void Seed() => CoreSettingsHooks.CloudBackup = () =>
    {
        var service = CoreSettings.Service;
        return service == null ? System.Threading.Tasks.Task.CompletedTask :
            Client.BackupAsync(service.Current, force: false, restoredFromBackup: service.RestoredFromBackup);
    };
}
