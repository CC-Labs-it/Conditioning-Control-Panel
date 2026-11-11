using ConditioningControlPanel.Services;
using ConditioningControlPanel.Avalonia.Platform;

namespace ConditioningControlPanel.Avalonia;

public partial class App
{
    // WPF App.xaml.cs:3262. Keep Core's stricter gate: only executed commands earn credit.
    // Wire the app-lifetime relay once at creation, independent of tab/window subscriptions.
    internal static RemoteRelay WireRemoteQuestProgress(RemoteRelay relay)
    {
        relay.CommandReceived += (_, action) =>
        {
            if (action == "trigger_panic") RemoteStopPass.Post();
            try { Quests?.TrackRemoteCommand(); }
            catch (System.Exception ex) { Serilog.Log.Warning(ex, "Remote quest credit failed"); }
        };
        relay.SessionEnded += (_, _) => { if (!CoreEngine.IsRunning) RemoteStopPass.Post(); };
        relay.ControllerConnectedChanged += (_, _) =>
        {
            if (relay.IsActive && !relay.ControllerConnected && CoreSettings.Current.StopEffectsOnRemoteDisconnect && !CoreEngine.IsRunning)
                RemoteStopPass.Post();
        };
        return relay;
    }
}
