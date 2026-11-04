using Avalonia.Threading;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Possession;
using ConditioningControlPanel.Services.Startup;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // WPF MainWindow.Lab.cs:690,1104: the offer is spent on open, not on filing.
        private void OfferPossessionRules() => Dispatcher.UIThread.Post(() =>
        {
            if (_lockdownVisualsClosed) return;
            try
            {
                var settings = CoreSettings.Current;
                if (!settings.LockdownPossessionEnabled || settings.LockdownPossessionIntroSeen) return;
                Platform.StartupLadder.PresentOrInbox(new InboxItem
                {
                    Key = "intro:possession",
                    Glyph = "🕶",
                    Title = "The warden's rules",
                    Summary = "What possession does before the room starts moving.",
                    Open = () =>
                    {
                        var live = CoreSettings.Current;
                        if (!live.LockdownPossessionIntroSeen)
                        {
                            live.LockdownPossessionIntroSeen = true;
                            CoreSettings.Save();
                        }
                        FeatureIntroPopup.ShowIfFirstTime("possession", this);
                    },
                });
                Platform.BarkHead.Engine?.Raise(PossessionBarkTriggers.Rules, guaranteed: true);
            }
            catch (System.Exception ex) { Log.Warning(ex, "Possession: failed to show the first-run rules"); }
        });
    }
}
