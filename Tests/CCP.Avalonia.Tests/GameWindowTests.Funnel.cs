using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Games;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class GameWindowTests
{
    [Fact]
    public void DtrhFunnelOpensKnownCheckoutOnlyAfterTheRun() => RunDtrh(true, (_, _, _, _) =>
    {
        ConditioningControlPanel.CoreSettings.Current.OfflineMode = true;
        var previous = GameWindow.DtrhOpenCheckout;
        int opened = 0;
        GameWindow.DtrhOpenCheckout = url => { Assert.Contains("patreon.com/checkout/CodeBambi", url); opened++; };
        try
        {
            var window = GameWindow.Launch("dtrh")!;
            window.HandleMessage("{\"type\":\"run-started\"}");
            const string message = "{\"type\":\"funnel\",\"event\":\"cta_clicked\",\"a\":\"dtrh\",\"b\":\"card\",\"plan\":\"prime\"}";
            window.HandleMessage(message);
            Assert.Equal(0, opened);
            window.HandleMessage("{\"type\":\"run-ended\"}");
            window.HandleMessage(message);
            window.HandleMessage(message);
            Assert.Equal(1, opened);
        }
        finally { GameWindow.DtrhOpenCheckout = previous; }
    });
}
