using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace ConditioningControlPanel.Avalonia.Views.Overlays;

internal sealed partial class MandatoryVideoOverlay
{
    // WPF VideoService.cs:5810-5822; ChaosModeService.cs:816-827 use delayed one-shot kicks.
    internal static Action<Action, TimeSpan> ScheduleTrailRaise = (a, delay) => _ = RaiseTrailAfterDelay(a, delay);

    private static async Task RaiseTrailAfterDelay(Action raise, TimeSpan delay)
    {
        await Task.Delay(delay).ConfigureAwait(false);
        Dispatcher.UIThread.Post(raise);
    }

    internal static void WireVibeTrailRestacking(Window w)
    {
        void Raise() => Dispatcher.UIThread.Post(Chaos.ChaosRunHost.RaiseVibeTrailAboveVideo);
        w.Opened += (_, _) =>
        {
            Raise();
            ScheduleTrailRaise(Raise, TimeSpan.FromMilliseconds(60));
        };
        w.AddHandler(InputElement.PointerPressedEvent, (_, _) => ScheduleTrailRaise(Raise, TimeSpan.FromMilliseconds(50)),
            global::Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
    }
}
