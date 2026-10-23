using System;
using Avalonia.Threading;

namespace ConditioningControlPanel.Avalonia;

public partial class App
{
    internal static void OnWebXpClaimed(double amount)
    {
        if (Achievements is { } achievements)
        {
            achievements.Progress.TotalXPEarned += amount;
            achievements.Save();
        }
        AnnounceFirstWebXpClaim(amount);
    }

    /// <summary>WPF ProgressionService.AnnounceFirstWebXpClaim: once per install, on the UI thread beside the toast.
    /// Uses the existing in-shell notification host (no additional window or panic surface).</summary>
    internal static void AnnounceFirstWebXpClaim(double amount) => Dispatcher.UIThread.Post(() =>
    {
        try
        {
            var s = CoreSettings.Current;
            if (s.SeenFeatureIntros.Contains("web-xp")) return;
            s.SeenFeatureIntros.Add("web-xp");
            CoreSettings.Save();
            Notifications.Show($"+{amount:0} XP from your web account - descending is account-wide now.",
                Helpers.NotificationType.Success, TimeSpan.FromSeconds(9));
        }
        catch (Exception ex) { Serilog.Log.Warning(ex, "Web XP notice failed to show"); }
    });
}
