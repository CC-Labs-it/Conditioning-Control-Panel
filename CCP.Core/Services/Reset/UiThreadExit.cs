using System;
using System.Threading;

// Services.Reset, not Services.Diagnostics: the WPF head has its own UiThreadExit there and sees Core's
// internals (InternalsVisibleTo), so the same full name would collide.
namespace ConditioningControlPanel.Services.Reset
{
    /// <summary>
    /// Leaving the process from inside a UI-thread handler without freezing it (WPF
    /// <c>Services/Diagnostics/UiThreadExit.cs</c>, main baf86f0ac; ccp-bugs #1458/#1468). Environment.Exit
    /// on the UI thread waits for the ProcessExit handlers, and one of them can wait on a Send to that same
    /// thread: a frozen window. So the exit runs on its own thread, the caller returns to its pump at once,
    /// and a backstop terminates the process after <see cref="GraceMs"/> if a handler wedges anyway.
    /// </summary>
    internal static class UiThreadExit
    {
        /// <summary>How long the orderly exit gets before the backstop terminates the process.</summary>
        internal const int GraceMs = FactoryResetSweep.ExitGraceMs;

        /// <summary>Starts the exit and returns at once. <paramref name="beforeTerminate"/> runs on the
        /// backstop thread just before it terminates. The two delegates after it exist for tests.</summary>
        internal static void Begin(int exitCode, Action? beforeTerminate = null,
            Action<int>? exit = null, Action<int>? terminate = null, int graceMs = GraceMs)
        {
            exit ??= Environment.Exit;
            terminate ??= _ => System.Diagnostics.Process.GetCurrentProcess().Kill();   // portable TerminateProcess

            StartBackground("CCP exit", () => exit(exitCode));
            StartBackground("CCP exit backstop", () =>
            {
                Thread.Sleep(Math.Max(0, graceMs));
                try { beforeTerminate?.Invoke(); } catch { }
                terminate(exitCode);
            });
        }

        private static void StartBackground(string name, Action body)
            => new Thread(() => { try { body(); } catch { } }) { IsBackground = true, Name = name }.Start();
    }
}
