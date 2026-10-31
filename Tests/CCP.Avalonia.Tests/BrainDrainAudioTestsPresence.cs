using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;
namespace CCP.Avalonia.Tests;
public class BrainDrainAudioTestsPresence
{
    [Fact]
    public void OnlyStoppingARunningPlayerClearsPresence()
    {
        var oldRpc = global::ConditioningControlPanel.Avalonia.App.DiscordRpc;
        using var rpc = new ConditioningControlPanel.Services.DiscordRichPresenceService();
        global::ConditioningControlPanel.Avalonia.App.DiscordRpc = rpc;
        var player = new BrainDrainPlayer((_, _) => null);
        try
        {
            CoreSettings.Current.BrainDrainEnabled = false;
            player.Start(); Assert.False(player.IsRunning);
            CoreSettings.Current.BrainDrainEnabled = true;
            player.Start(); Assert.Equal("Deep conditioning", rpc.CurrentState);
            player.Stop(); Assert.Equal("Browsing", rpc.CurrentState);
            rpc.SetVideoActivity(); var video = rpc.CurrentState;
            player.Stop(); Assert.Equal(video, rpc.CurrentState);
        }
        finally { player.Stop(); global::ConditioningControlPanel.Avalonia.App.DiscordRpc = oldRpc; }
    }
}
