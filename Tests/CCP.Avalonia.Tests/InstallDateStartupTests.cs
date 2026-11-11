using System.IO;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using Newtonsoft.Json.Linq;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;
public class InstallDateStartupTests
{
    [Fact]
    public void StartupRecordsDateOnlyOnceInSandboxSettings()
    {
        var path = Path.Combine(CorePaths.UserData, "settings.json");
        Assert.Equal(TestUserDataProfile.Root, CorePaths.UserData);
        if (File.Exists(path)) File.Delete(path);
        var service = new SettingsService();
        AvApp.RecordInstallDate(service);
        var first = (string?)JObject.Parse(File.ReadAllText(path))["InstallDate"];
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", first!);
        service.Current.InstallDate = "2021-02-03";
        service.SaveImmediate();
        AvApp.RecordInstallDate(new SettingsService());
        Assert.Equal("2021-02-03", (string?)JObject.Parse(File.ReadAllText(path))["InstallDate"]);
    }
}
