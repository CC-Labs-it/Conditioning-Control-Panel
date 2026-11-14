using System;
using System.IO;
using Avalonia.Threading;
using ConditioningControlPanel.Services.EmiDesk;

namespace ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;

// WPF EmiCodex.cs:636-701: one delayed offer, with brakes checked at the tick.
internal static class EmiBookOffer
{
    private static DispatcherTimer? _timer;
    internal static int DelayMs = 20_000;
    internal static Func<bool> HasContent = () => EmiCodexRules.HasContent(
        Path.Combine(AppContext.BaseDirectory, "Resources", "web", "codex"));
    internal static Func<bool> IsOpen = () => EmiDeskService.Instance.Window?.Book != null;
    internal static Func<int> Opens = () => EmiState.Current.CodexOpens;

    internal static void Schedule(string why)
    {
        Stop();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Math.Max(1, DelayMs)) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (!ReferenceEquals(_timer, timer)) return;
            _timer = null;
            try
            {
                if (EmiCodexRules.OfferRefusal(HasContent(), IsOpen(), Opens) == null)
                    EmiDeskService.Instance.Fire("bookOffer", new { why });
            }
            catch (Exception ex) { Serilog.Log.Debug(ex, "[EmiDesk] delayed book offer failed"); }
        };
        _timer = timer;
        timer.Start();
    }

    internal static void Stop() { _timer?.Stop(); _timer = null; }
}
