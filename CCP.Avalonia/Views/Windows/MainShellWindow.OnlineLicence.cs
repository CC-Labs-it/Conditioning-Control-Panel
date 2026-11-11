using System;
using System.Linq;
using System.Threading.Tasks;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Services.Fyp.Online;
using ConditioningControlPanel.Services.Startup;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

/// <summary>
/// The one-time licence card (WPF MainWindow.OnlineMeter.cs OfferOnlineLicenceCard / OnlineOfficialNice, #2503).
/// WPF queues it on the startup ladder's modal pump at priority 40 (after What's New, 30). This head has no general
/// modal pump yet (MainShellWindow.UpgradeIntro.cs), so the card waits the way the display-name ask does: the
/// 750 ms settle, then until no startup modal, update dialog or tour is up and What's New has drained
/// (<see cref="StartupLadder.ModalPending"/>). A card that never got its turn is offered again next launch.
/// <para>The ladder's modal pump does NOT wait for a session or a Lockdown, and the flag can flip mid-run, so the
/// card asks the same hold the spent notice does: while it holds nothing is queued, and the minute clock offers it
/// again once the hold ends. A card queued before a session started steps aside at show time.</para>
/// </summary>
public partial class MainShellWindow
{
    private bool _onlineLicenceCardQueued;
    private bool _onlineLicenceCancelled;
    private OnlineLicenceCard? _onlineLicenceCard;
    internal Func<TimeSpan, Task> OnlineLicenceDelay = Task.Delay;
    internal Task? OnlineLicenceTask;

    /// <summary>Called on every meter refresh (WPF RefreshOnlineMeter :163).</summary>
    internal void OfferOnlineLicenceCard(OnlineMeterState st)
    {
        if (!st.ShowOfficialNote || _onlineLicenceCardQueued || FriendsLanding.World().Holding) return;
        _onlineLicenceCardQueued = true;
        OnlineLicenceTask = RunOnlineLicenceCardAsync();
    }

    private bool OnlineLicenceReady() => !StartupLadder.ModalPending && StartupQueueCore.CanStartModal(
        IsStartupDialogShowing || OwnedWindows.Any(w => w.IsVisible && w.IsDialog),
        AppUpdater.IsUpdateDialogActive, CoreTutorial.IsActive, IsVisible && CoreSettings.Current.HasAcceptedAgeVerification);

    private async Task RunOnlineLicenceCardAsync()
    {
        try
        {
            // WPF StartupPresenter: 750 ms settle, 500 ms recheck, five minutes per surface.
            await OnlineLicenceDelay(TimeSpan.FromMilliseconds(750));
            var waited = TimeSpan.Zero;
            while (!_onlineLicenceCancelled && waited < TimeSpan.FromMinutes(5) && !OnlineLicenceReady())
            {
                await OnlineLicenceDelay(TimeSpan.FromMilliseconds(500));
                waited += TimeSpan.FromMilliseconds(500);
            }
            if (_onlineLicenceCancelled || !OnlineLicenceReady()) return;   // owed next launch

            // The flag or the source may have changed while it waited for its turn.
            var now = OnlineMeterHead.Now();
            if (!now.ShowOfficialNote) return;
            if (FriendsLanding.World().Holding) { _onlineLicenceCardQueued = false; return; }

            IsStartupDialogShowing = true;
            try
            {
                _onlineLicenceCard = new OnlineLicenceCard(now.Plan);
                await _onlineLicenceCard.ShowDialogSafe(this);
            }
            finally
            {
                _onlineLicenceCard = null;
                IsStartupDialogShowing = false;
                StartupLadder.PumpHeld();
            }
            if (_onlineLicenceCancelled) return;   // a panic closed it: offered again next launch
            OnlineOfficialNice();
        }
        catch (Exception ex) { Log.Warning(ex, "Online licence card failed"); }
    }

    /// <summary>"Nice": the note folds away for good on this PC; the stamped line stays (WPF OnlineOfficialNice :480).</summary>
    private void OnlineOfficialNice()
    {
        try
        {
            CoreSettings.Current.ScrolllerOfficialNoteSeen = true;
            CoreSettings.Save();
            OnlineMeterHead.Poke();
        }
        catch (Exception ex) { Log.Debug("Official note Nice: {E}", ex.Message); }
    }

    /// <summary>Panic (stricter than WPF, like the What's New modal): the card closes unseen and is owed next launch.</summary>
    internal static void CloseOnlineLicenceCardForPanic(MainShellWindow? shell)
    {
        var owner = shell ?? Current;
        if (owner == null) return;
        owner._onlineLicenceCancelled = true;
        owner._onlineLicenceCard?.Close();
    }
}
