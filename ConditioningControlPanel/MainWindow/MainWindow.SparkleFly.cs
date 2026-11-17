using System.Windows;
using System.Windows.Controls;
using ConditioningControlPanel.Services.Prizes;

namespace ConditioningControlPanel;

/// <summary>The panel's side of the Sparkles flight: stars land on the header wallet's star.</summary>
public partial class MainWindow
{
    private PanelSparkleFlightHost? _sparkleFlightHost;

    private void InitializeSparkleFly()
    {
        _sparkleFlightHost ??= new PanelSparkleFlightHost(this);
        SparkleFlightDirector.Register(_sparkleFlightHost);
    }

    private void ShutdownSparkleFly()
    {
        if (_sparkleFlightHost != null) SparkleFlightDirector.Unregister(_sparkleFlightHost);
    }

    private sealed class PanelSparkleFlightHost : ISparkleFlightHost
    {
        private readonly MainWindow _w;
        public PanelSparkleFlightHost(MainWindow w) => _w = w;
        public Window Window => _w;
        public Panel? FlightPanel => _w.EventFxHost;
        public FrameworkElement? WalletStar => _w.HeaderSparkleWallet?.Star;
        // A level-up came from "you": the profile bubble, the same honest origin THE BANK uses.
        public FrameworkElement? OriginFor(SparkleSource source) => source == SparkleSource.LevelUp ? _w.BtnProfileBubble : null;
        public void HoldCount(int value) => _w.HeaderSparkleWallet?.HoldShown(value);
        public void ReleaseCount() => _w.HeaderSparkleWallet?.ReleaseShown();
        public void Kick(bool settle) => _w.HeaderSparkleWallet?.Kick(settle);
    }
}
