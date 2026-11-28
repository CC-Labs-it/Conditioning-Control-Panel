using System.IO;

namespace ConditioningControlPanel.Services.Dares;

/// <summary>WPF service wiring, kept on the dispatcher.</summary>
public static class DaresServiceApp
{
    public static DaresService CreateForApp(out DareMeter meter)
    {
        var host = new AppDareHost();
        meter = new DareMeter(new DareMeter.FileStore(Path.Combine(App.UserDataPath, "dares_meter.json")));
        var keeper = new DareLockdownKeeper(host,
            new DareLockdownKeeper.FileStore(Path.Combine(App.UserDataPath, "dares_lockdown_serve.json")));
        return new DaresService(new DaresApi(), host, meter, keeper);
    }
}
