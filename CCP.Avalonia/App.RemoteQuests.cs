using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia;

public partial class App
{
    // WPF App.xaml.cs:3262. Keep Core's stricter gate: only executed commands earn credit.
    // Wire the app-lifetime relay once at creation, independent of tab/window subscriptions.
    internal static RemoteRelay WireRemoteQuestProgress(RemoteRelay relay)
    {
        relay.CommandReceived += (_, _) =>
        {
            try { Quests?.TrackRemoteCommand(); }
            catch (System.Exception ex) { Serilog.Log.Warning(ex, "Remote quest credit failed"); }
        };
        return relay;
    }
}
