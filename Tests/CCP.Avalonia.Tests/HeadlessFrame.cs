using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;

namespace CCP.Avalonia.Tests;

internal static class HeadlessFrame
{
    // RunJobs/UpdateLayout alone do not publish the compositor's hit-test geometry.
    // Drain Loaded/BringIntoView first, then render the arranged tree before pointer input.
    internal static void Publish(TopLevel host)
    {
        Dispatcher.UIThread.RunJobs();
        host.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }
}
