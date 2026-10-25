using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace CCP.Avalonia.Testing;

/// <summary>
/// Runs each Avalonia test assembly on one long-lived dispatcher thread. Windows native controls
/// require the thread that creates them to be STA; keeping the dispatcher alive also keeps every
/// async continuation and later test action on the same thread.
/// </summary>
internal static class AvaloniaTestDispatcher
{
    private static readonly object Gate = new();
    private static readonly CancellationTokenSource Lifetime = new();
    private static Dispatcher? _dispatcher;

    static AvaloniaTestDispatcher() =>
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Lifetime.Cancel();

    internal static bool IsDispatcherThread =>
        Volatile.Read(ref _dispatcher)?.CheckAccess() == true;

    internal static Task RunAsync(Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        var dispatcher = Volatile.Read(ref _dispatcher);
        if (dispatcher is not null)
            return dispatcher.CheckAccess() ? action() : Enqueue(dispatcher, action);

        var completion = NewCompletion();
        lock (Gate)
        {
            dispatcher = _dispatcher;
            if (dispatcher is null)
            {
                Start(action, completion);
                return completion.Task;
            }
        }

        return dispatcher.CheckAccess() ? action() : Enqueue(dispatcher, action);
    }

    internal static void Run(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        RunAsync(() =>
        {
            action();
            return Task.CompletedTask;
        }).GetAwaiter().GetResult();
    }

    private static TaskCompletionSource<object?> NewCompletion() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static void Start(Func<Task> action, TaskCompletionSource<object?> completion)
    {
        var thread = new Thread(() => Bootstrap(action, completion))
        {
            IsBackground = true,
            Name = "CCP Avalonia test dispatcher",
        };
        if (OperatingSystem.IsWindows())
            thread.SetApartmentState(ApartmentState.STA);

        thread.Start();
    }

    private static void Bootstrap(Func<Task> action, TaskCompletionSource<object?> completion)
    {
        // These are the same public Avalonia primitives used by the desktop head. Installing them
        // before AppBuilder setup means setup, native control attachment, and async continuations
        // all see this thread rather than the xUnit worker that requested the test.
        AvaloniaSynchronizationContext.InstallIfNeeded();
        var dispatcher = Dispatcher.UIThread;
        Volatile.Write(ref _dispatcher, dispatcher);
        var shutdownStarted = 0;
        dispatcher.ShutdownStarted += (_, _) => Volatile.Write(ref shutdownStarted, 1);

        Execute(action, completion);
        try
        {
            dispatcher.MainLoop(Lifetime.Token);
        }
        catch (InvalidOperationException) when (Volatile.Read(ref shutdownStarted) != 0)
        {
            // A test may deliberately shut down its desktop lifetime. Avalonia's MainLoop throws
            // when entered after that shutdown; the action's completion already carried its result.
        }
    }

    private static Task Enqueue(Dispatcher dispatcher, Func<Task> action)
    {
        var completion = NewCompletion();
        try
        {
            dispatcher.Post(() => Execute(action, completion), DispatcherPriority.Normal);
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
        }
        return completion.Task;
    }

    private static void Execute(Func<Task> action, TaskCompletionSource<object?> completion)
    {
        Task task;
        try
        {
            task = action() ?? Task.CompletedTask;
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
            return;
        }

        if (task.IsCompleted)
        {
            Complete(task, completion);
            return;
        }

        _ = task.ContinueWith(
            completed => Complete(completed, completion),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private static void Complete(Task task, TaskCompletionSource<object?> completion)
    {
        if (task.IsCanceled)
        {
            completion.TrySetCanceled();
            return;
        }

        if (task.IsFaulted)
        {
            completion.TrySetException(task.Exception!.InnerExceptions);
            return;
        }

        completion.TrySetResult(null);
    }
}

/// <summary>
/// Every Avalonia test host links this file. Point XDG_CONFIG_HOME at a throwaway folder before any
/// test runs: building a GeneralSettingsSection (any MainShellWindow does) reconciles the session
/// autostart entry under $XDG_CONFIG_HOME/autostart, and ApplicationData resolves there too, so
/// without this a test reads, adopts or writes the developer's real ~/.config.
/// </summary>
internal static class TestXdgConfigSandbox
{
    internal static readonly string Root = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(), "ccp-test-xdg-" + Guid.NewGuid().ToString("N"));

    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Initialize()
    {
        System.IO.Directory.CreateDirectory(Root);
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", Root);
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { System.IO.Directory.Delete(Root, recursive: true); } catch { }
        };
    }
}

/// <summary>
/// Before running anything, xUnit's VSTest adapter launches the test exe with <c>-assemblyInfo</c>, waits for it
/// to exit and parses ALL of its stdout as one JSON object. xUnit v3 3.2.2's <c>ConsoleRunner.Run</c> also queues
/// a 1 s timer once Main's work is done that prints "Waiting 10 seconds for foreground threads to exit..." to
/// stdout if the process is still alive. A teardown slower than 1 s (a loaded Windows runner) therefore appends
/// that line and discovery dies with "Test process did not return valid JSON (non-object)", 0 tests run.
/// xUnit 4.0 skips the notice in assemblyInfo mode (<c>if (!runner.assemblyInfoMode)</c>); this does the same at
/// the stdout boundary: in an <c>-assemblyInfo</c> launch only the first line (the JSON) reaches stdout.
/// </summary>
internal static class AssemblyInfoStdoutGuard
{
    /// <summary>Test seam: hold a foreground thread until xUnit's late line was written (replays a slow teardown).</summary>
    internal const string HoldVariable = "CCP_TEST_HOLD_ASSEMBLYINFO_EXIT";
    internal const string LateLineMarker = "ccp-assemblyinfo-late-line";

    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Initialize()
    {
        if (!IsAssemblyInfoLaunch())
            return;

        var writer = new FirstLineOnlyWriter(Console.Out);
        Console.SetOut(writer);
        if (Environment.GetEnvironmentVariable(HoldVariable) != "1")
            return;

        new Thread(() =>
        {
            writer.LateLine.Wait();
            Console.Error.WriteLine(LateLineMarker);
        })
        { IsBackground = false, Name = "CCP assemblyInfo teardown hold" }.Start();
    }

    /// <summary>The adapter passes its arguments as <c>@@ &lt;response file&gt;</c> (xUnit
    /// OutOfProcessTestProcessLauncherBase.Launch), one argument per line; read them the way xUnit does.</summary>
    private static bool IsAssemblyInfoLaunch()
    {
        var args = Environment.GetCommandLineArgs();
        try
        {
            if (args.Length == 3 && args[1] == "@@" && System.IO.File.Exists(args[2]))
                args = System.IO.File.ReadAllLines(args[2]);
        }
        catch (Exception)
        {
            return false;   // xUnit reports an unreadable response file itself
        }

        return Array.Exists(args,
            arg => string.Equals(arg.Trim(), "-assemblyInfo", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class FirstLineOnlyWriter(System.IO.TextWriter inner) : System.IO.TextWriter
    {
        private bool _firstLineWritten;

        internal ManualResetEventSlim LateLine { get; } = new();

        public override System.Text.Encoding Encoding => inner.Encoding;

        public override void Write(char value)
        {
            if (_firstLineWritten)
            {
                if (value == '\n') LateLine.Set();
                return;
            }

            inner.Write(value);
            if (value != '\n') return;
            inner.Flush();
            _firstLineWritten = true;
        }

        public override void Flush() => inner.Flush();
    }
}
