// PORTED from ConditioningControlPanel/MainWindow/MainWindow.xaml.cs ArmPanicWatchdog (:1025) and
// RunEmergencyPanicTeardown (:1046), #919b. The listener (Win32PanicKey / X11PanicKey / the portal) runs on its own
// thread here, so a wedged UI thread can no longer lose the key itself; it can still sit on the queued
// handler. This watches that handler from a background thread and, if it has not finished inside
// 2 s, runs the thread-safe part of the stop off the UI thread.
// The screen OCR stops here too and its restart is queued (WPF :1157, :1192; restack b221, #4551).

using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Services;

using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

internal static class PanicWatchdog
{
    /// <summary>WPF PanicWatchdogTimeout.</summary>
    internal static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    private static int _fallbackRunning;
    internal static Action<Action> PostRecovery { get; set; } = action => Dispatcher.UIThread.Post(action);   // tests

    /// <summary>WPF's Dispatcher.HasShutdownStarted guard (MainWindow.xaml.cs:1073). Avalonia exposes only the
    /// ShutdownStarted event, so the flag is latched from it: a fallback never races the app's own teardown.</summary>
    private static volatile bool _shutdownStarted;
    private static int _shutdownHooked;

    /// <summary>Test seam for the shutdown latch.</summary>
    internal static bool ShutdownStartedForTest { set => _shutdownStarted = value; }

    private static void HookShutdown()
    {
        if (Interlocked.Exchange(ref _shutdownHooked, 1) == 1) return;
        try { Dispatcher.UIThread.ShutdownStarted += (_, _) => _shutdownStarted = true; }
        catch (Exception ex) { Log.Debug(ex, "Panic watchdog: could not watch dispatcher shutdown"); }
    }

    /// <summary>Test seam: the off-thread teardown (swapped to observe it without real services).</summary>
    internal static Action Teardown { get; set; } = RunEmergencyTeardown;

    /// <summary>Queues <paramref name="handler"/> on the UI thread and watches it. Safe from any thread;
    /// returns at once (the hook callback must never wait).</summary>
    internal static void QueueWatched(Action handler)
    {
        HookShutdown();
        DispatcherOperation op;
        Interlocked.Increment(ref _pending);
        try { op = Dispatcher.UIThread.InvokeAsync(() => { try { handler(); } finally { Interlocked.Decrement(ref _pending); } }); }
        catch (Exception ex)
        {
            Interlocked.Decrement(ref _pending);
            // The dispatcher refused the post (shutting down or broken): the press must still stop things.
            Log.Error(ex, "Panic: could not queue the handler; tearing down off-thread");
            _ = Task.Run(RunGuarded);   // this can be the hook or the D-Bus reader, which must never wait
            return;
        }
        Arm(op.GetTask());
    }

    private static int _pending;

    /// <summary>A panic press is queued on the UI thread and has not run yet. Whatever would start
    /// something on the same thread meanwhile (the Chaos countdown's BeginRun) waits for it.</summary>
    internal static bool PanicPending => Volatile.Read(ref _pending) > 0;

    /// <summary>WPF ArmPanicWatchdog: a background watcher, never a wait on the calling thread.</summary>
    internal static Task Arm(Task handler, TimeSpan? timeout = null)
    {
        // Attached here, not after the wait: a handler can still throw once the fallback has run.
        handler.ContinueWith(t => Log.Error(t.Exception, "Panic: the press handler threw"),
            CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
        return Task.Run(async () =>
        {
            try
            {
                var finished = await Task.WhenAny(handler, Task.Delay(timeout ?? Timeout)).ConfigureAwait(false);
                if (finished == handler) return;   // as WPF: a thrown handler is logged and not torn down
                RunGuarded();
            }
            catch (Exception ex)
            {
                try { Log.Error(ex, "Panic watchdog failed"); } catch { }
            }
        });
    }

    private static void RunGuarded()
    {
        if (_shutdownStarted)
        {
            try { Log.Information("PANIC FALLBACK skipped - the app is already shutting down"); } catch { }
            return;
        }
        if (Interlocked.Exchange(ref _fallbackRunning, 1) == 1) return;
        int? suspension = null;
        try
        {
            try { suspension = KeywordTriggerHead.SuspendOcrForPanic(); }
            catch (Exception ex) { try { Log.Warning(ex, "Panic OCR stop failed"); } catch { } }
            try
            {
                Log.Warning("PANIC FALLBACK: the UI thread did not handle the panic key within {Ms}ms - tearing down off-thread",
                    (int)Timeout.TotalMilliseconds);
            }
            catch { }
            Teardown();
        }
        catch (Exception ex)
        {
            try { Log.Error(ex, "Panic fallback teardown failed"); } catch { }
        }
        finally
        {
            Interlocked.Exchange(ref _fallbackRunning, 0);
            try
            {
                if (suspension is int id) PostRecovery(() =>
                {
                    try { KeywordTriggerHead.RecoverOcrAfterPanic(id); }
                    catch (Exception ex) { try { Log.Warning(ex, "Panic OCR recovery failed"); } catch { } }
                });
            }
            catch (Exception ex) { try { Log.Warning(ex, "Panic OCR recovery could not be queued"); } catch { } }
        }
    }

    /// <summary>WPF RunEmergencyPanicTeardown: ONLY thread-safe steps, each guarded on its own. No overlay
    /// hiding: overlays are the UI thread's to drop when it comes back and runs the real handler.</summary>
    private static void RunEmergencyTeardown()
    {
        Step("race engine download", RaceEngineDownload.Cancel);
        Step("Linux race", () => Views.Games.LinuxRaceHost.Current?.Kill());
        Step("haptics", () => CoreHaptics.Service?.PanicStop());
        Step("remote haptics", RemoteCommands.StopHaptics);
        // WPF RunEmergencyPanicTeardown (main 7e14c2870): the hold the real handler would open first; thread-safe.
        Step("chaster hold", () => ConditioningControlPanel.Services.Safety.PanicChasterHold.Open("panic fallback"));
        Step("ai follow-ups", () => ConditioningControlPanel.Services.Commands.AiCommandService.CancelAll());
        // The quiet the real handler arms (PanicAftermath, thread-safe): nothing AI-driven starts meanwhile.
        Step("panic quiet", () => ConditioningControlPanel.Services.Safety.PanicAftermath.ArmQuiet());
        // Conditional, as WPF (#668): with the standalone Audio Layers master on, the bed is the user's.
        Step("audio layers", () => { if (!CoreSettings.Current.AudioLayersEnabled) LayeredAudio.Instance?.Stop(); });
        Step("brain drain", () => BrainDrainPlayer.Instance?.Stop());
        Step("audio playlist", App.StopAudioPlaylist); // WPF MainWindow.xaml.cs:1117
        Step("mantra chant", () => App.MantraChant.StopEmergency()); // WPF MainWindow.xaml.cs:1112
        Step("mind wipe", () => CoreMindWipe.StopProvider?.Invoke());
        Step("unduck", () => LibVlcAudio.Instance?.ForceUnduck());
        Log.Information("PANIC FALLBACK complete");
    }

    private static void Step(string name, Action step)
    {
        try { step(); }
        catch (Exception ex) { try { Log.Warning("PANIC FALLBACK: {Step} step failed: {Error}", name, ex.Message); } catch { } }
    }
}
