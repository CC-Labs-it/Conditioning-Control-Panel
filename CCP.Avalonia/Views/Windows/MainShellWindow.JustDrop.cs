// PORTED from ConditioningControlPanel/MainWindow/MainWindow.JustDrop.cs: whether the Just Drop door
// exists for this account (Core JustDropService.DoorAvailable, a per-launch server GET with a false
// default and no persistence) and telling the shell when that changes. The refusal and the launch
// live in ShowTab("justdrop") -> OpenJustDrop, one gate for every caller (palette row, Premium shelf).
//
// ApplyJustDropDoorVisibility fans the flag out to WPF's one other surface, the Takeaway shelf
// (WPF :118 RefreshTakeawayShelf); this head's shelf still paints only WPF's empty state
// (MainShellWindow.Takeaway.cs, no JustDropOrdersService here), so the fan-out is the log line.

using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Services.JustDrop;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>WPF :30: behind the marquee (3 s), update banner (5 s) and announcement (7 s).</summary>
        private static readonly TimeSpan JustDropCheckDelay = TimeSpan.FromMilliseconds(9000);

        /// <summary>The server check. Seeded by App startup with JustDropService.RefreshAvailabilityAsync;
        /// null in headless tests, so opening a shell never reaches the network.</summary>
        internal static Func<Task>? JustDropAvailabilityCheck;

        private bool _justDropDoorHooked;

        /// <summary>WPF :39 InitializeJustDropDoor: paint, subscribe once, kick the one delayed check.</summary>
        private void InitializeJustDropDoor()
        {
            try
            {
                ApplyJustDropDoorVisibility();
                if (!_justDropDoorHooked)
                {
                    JustDropService.AvailabilityChanged += OnJustDropAvailabilityChanged;
                    Closed += (_, _) => JustDropService.AvailabilityChanged -= OnJustDropAvailabilityChanged;
                    _justDropDoorHooked = true;
                }
                var check = JustDropAvailabilityCheck;
                if (check == null) return;
                DispatcherTimer.RunOnce(() => _ = Task.Run(async () =>
                {
                    try { await check().ConfigureAwait(false); }
                    catch (Exception ex) { Log.Debug("JustDrop: availability kickoff failed: {E}", ex.Message); }
                }), JustDropCheckDelay);
            }
            catch (Exception ex) { Log.Warning(ex, "InitializeJustDropDoor failed; the door stays hidden"); }
        }

        private void OnJustDropAvailabilityChanged(object? sender, EventArgs e)
        {
            try { ApplyJustDropDoorVisibility(); }
            catch (Exception ex) { Log.Warning(ex, "OnJustDropAvailabilityChanged failed"); }
        }

        private void ApplyJustDropDoorVisibility() =>
            Log.Debug("JustDrop door visibility -> {State}", JustDropService.DoorAvailable ? "visible" : "hidden");

        /// <summary>WPF ShowTab("justdrop") -> JustDropHostService.LaunchShop. The window's gate
        /// (GameWindow.JustDropDoorOpen) refuses an unavailable door as a no-op; a live one is refocused.</summary>
        internal GameWindow? OpenJustDrop() => GameWindow.Launch(GameWindow.JustDropId);
    }
}
