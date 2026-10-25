using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace ConditioningControlPanel.Services.Diagnostics
{
    /// <summary>
    /// Leaving the process from inside a UI-thread handler without freezing it (ccp-bugs #1458,
    /// #1468, #1469, #1471, #1472: the same 7.1.9 hang report in all five).
    ///
    /// <para>The dispatcher's render-failure branch called <c>Environment.Exit(1)</c> on the UI
    /// thread. The runtime runs AppDomain.ProcessExit on the FINALIZER thread and makes the exiting
    /// thread wait for it. One of those handlers is <c>Microsoft.Win32.SystemEvents.Shutdown</c>,
    /// which joins the SystemEvents window thread; that thread was parked in
    /// <c>DispatcherSynchronizationContext.Send</c>, delivering a system event (a display change, the
    /// very thing that kills the render thread) to the UI thread. UI waits for finalizer, finalizer
    /// waits for SystemEvents, SystemEvents waits for UI: the "hard exit to prevent cascade" became a
    /// frozen window until the user killed it. The hang report shows all three stacks.</para>
    ///
    /// <para>So the exit runs on its own thread and the caller returns to its pump at once (the
    /// pending Send completes, the join returns, ProcessExit finishes: toys stopped, memory saved,
    /// log footer written). A backstop terminates the process after <see cref="GraceMs"/> if any
    /// ProcessExit handler wedges anyway.</para>
    /// </summary>
    internal static class UiThreadExit
    {
        /// <summary>How long the orderly exit gets before the backstop terminates the process.</summary>
        internal const int GraceMs = 5000;

        private const int RenderThreadFailureHResult = unchecked((int)0x88980406);

        /// <summary>A failure of WPF's render thread (or an OOM in the composition channel): the app
        /// cannot draw again, so the dispatcher handler leaves instead of showing a dialog.</summary>
        internal static bool IsRenderFailure(Exception? ex)
        {
            if (ex == null) return false;
            if (ex is OutOfMemoryException) return true;
            if (ex.HResult == RenderThreadFailureHResult) return true;
            var message = ex.Message ?? "";
            return message.Contains("RENDER") || message.Contains("0x88980406");
        }

        /// <summary>
        /// Starts the exit and returns at once. Never blocks the caller, so it is safe from a
        /// dispatcher handler. <paramref name="beforeTerminate"/> runs on the backstop thread just
        /// before it terminates (flush the log there). The two delegates after it exist for tests.
        /// </summary>
        internal static void Begin(int exitCode, Action? beforeTerminate = null,
            Action<int>? exit = null, Action<int>? terminate = null, int graceMs = GraceMs)
        {
            exit ??= Environment.Exit;
            terminate ??= TerminateSelf;

            StartBackground("CCP exit", () => exit(exitCode));
            StartBackground("CCP exit backstop", () =>
            {
                Thread.Sleep(Math.Max(0, graceMs));
                try { beforeTerminate?.Invoke(); } catch { }
                terminate(exitCode);
            });
        }

        private static void StartBackground(string name, Action body)
        {
            var thread = new Thread(() =>
            {
                try { body(); } catch { }
            })
            { IsBackground = true, Name = name };
            thread.Start();
        }

        private static void TerminateSelf(int exitCode)
        {
            try { TerminateProcess(GetCurrentProcess(), unchecked((uint)exitCode)); } catch { }
        }

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);
    }
}
