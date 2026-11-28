using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Logging;
using ConditioningControlPanel.Avalonia.Views.Dialogs;

namespace ConditioningControlPanel.Avalonia.Platform;

// WPF App.xaml.cs:2468, 2537, 2554, 5955 and 6041.
internal sealed class CrashLog
{
    private readonly string _directory;
    private readonly string _version;
    private readonly Action<Exception> _showError;
    private readonly Action _beginExit;
    private readonly Dictionary<string, int> _counts = new();
    private readonly object _gate = new();
    private bool _dialogShown;
    private int _exitInProgress;

    internal CrashLog(string directory, string version, Action<Exception> showError, Action? beginExit = null)
    { _directory = directory; _version = version; _showError = showError; _beginExit = beginExit ?? BeginExit; }

    internal static CrashLog? Current;

    internal static void Install()
    {
        Current = new CrashLog(Path.Combine(CorePaths.UserData, "logs"), CoreReleaseContent.AppVersion, ShowError);
        Current.Rotate();
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Current?.DomainFault(e, Serilog.Log.CloseAndFlush);
        TaskScheduler.UnobservedTaskException += Current.TaskFault;
    }

    // The tray subscribes later; put this handler after it whenever a tray is created.
    internal static void RegisterDispatcherLast()
    {
        Dispatcher.UIThread.UnhandledException -= OnDispatcher;
        Dispatcher.UIThread.UnhandledException += OnDispatcher;
        _dispatcherAttached = true;
    }

    private static bool _dispatcherAttached;

    /// <summary>Test seam: detach the shared dispatcher handler (a tray built by another test attaches it);
    /// true when it was attached.</summary>
    internal static bool DetachDispatcher()
    {
        var was = _dispatcherAttached;
        Dispatcher.UIThread.UnhandledException -= OnDispatcher;
        _dispatcherAttached = false;
        return was;
    }

    private static void OnDispatcher(object? sender, DispatcherUnhandledExceptionEventArgs e) => Current?.DispatcherFault(sender, e);

    internal void DispatcherFault(object? sender, DispatcherUnhandledExceptionEventArgs e)
    {
        if (e.Handled) return;
        e.Handled = true;
        Write("DISPATCHER", e.Exception);
        // WPF App.xaml.cs:2474: desktop quota exhaustion drops a frame, not the app.
        if (e.Exception is System.ComponentModel.Win32Exception quota &&
            (quota.NativeErrorCode == 1816 || quota.NativeErrorCode == 1450 ||
             quota.Message.Contains("Not enough quota", StringComparison.Ordinal)))
        {
            try { Serilog.Log.Warning("Window-show quota exhausted, frame dropped"); } catch { }
            return;
        }
        if (IsRenderFailure(e.Exception))
        {
            if (Interlocked.Exchange(ref _exitInProgress, 1) == 0) _beginExit();
            return;
        }
        if (Volatile.Read(ref _exitInProgress) != 0 || _dialogShown) return;
        _dialogShown = true;
        try { _showError(e.Exception); } catch { }
    }

    // WPF Services/Diagnostics/UiThreadExit.cs:35 and App.xaml.cs:2493.
    internal static bool IsRenderFailure(Exception ex) => ex is OutOfMemoryException
        || ex.HResult == unchecked((int)0x88980406)
        || ex.Message.Contains("UCEERR_", StringComparison.Ordinal)
        || ex.Message.Contains("0x88980406", StringComparison.Ordinal);

    private static void BeginExit() => BeginExit(() =>
    {
        try { Serilog.Log.CloseAndFlush(); } catch { }
        Environment.Exit(1);
    }, () => Environment.FailFast("Unrecoverable render failure"), 5000);

    // Never block the dispatcher: an exit handler can be waiting for it. The backstop
    // runs independently of both log flushing and ProcessExit, on every platform.
    internal static void BeginExit(Action exit, Action terminate, int graceMs)
    {
        var backstop = new Thread(() => { Thread.Sleep(Math.Max(0, graceMs)); terminate(); })
        { IsBackground = true, Name = "CCP exit backstop" };
        backstop.Start();
        var worker = new Thread(() => { try { exit(); } catch { } })
        { IsBackground = true, Name = "CCP exit" };
        worker.Start();
    }

    private static void ShowError(Exception e)
    {
        var dialog = new MessageDialog(Loc.Get("title_unexpected_error"), Loc.GetF("msg_unexpected_error", e.Message), false);
        var owner = (global::Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        _ = dialog.ShowDialogSafe(owner);
    }

    internal void DomainFault(UnhandledExceptionEventArgs e, Action flush)
    {
        Write("DOMAIN", e.ExceptionObject as Exception);
        if (e.IsTerminating) { try { flush(); } catch { } }
    }

    internal void TaskFault(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        e.SetObserved();
        var inner = e.Exception.Flatten().InnerExceptions;
        if (inner.Count > 0 && inner.All(x => x is WebSocketException ws && ws.WebSocketErrorCode == WebSocketError.InvalidState &&
            ((x.StackTrace ?? "").Contains("WebSocket.CloseAsync", StringComparison.Ordinal) ||
             (x.StackTrace ?? "").Contains("WebSocket.CloseOutputAsync", StringComparison.Ordinal)))) return;
        Write("TASK", e.Exception);
    }

    internal void Rotate()
    {
        try
        {
            Directory.CreateDirectory(_directory);
            var path = Path.Combine(_directory, "crash.log");
            var marker = Path.Combine(_directory, "crash.log.version");
            var previous = File.Exists(marker) ? File.ReadAllText(marker).Trim() : "";
            if (File.Exists(path) && (previous != _version || new FileInfo(path).Length > 512 * 1024))
            {
                try { File.Move(path, path + ".prev", true); }
                catch { File.WriteAllText(path, ""); }
            }
            File.WriteAllText(marker, _version);
        }
        catch { }
    }

    internal void Write(string source, Exception? ex)
    {
        if (ex == null) return;
        try
        {
            var top = (ex.StackTrace ?? "").Split('\n')[0].Trim();
            var signature = $"{source}|{ex.GetType().FullName}|{ex.Message}|{top}";
            lock (_gate)
            {
                _counts.TryGetValue(signature, out var count);
                _counts[signature] = ++count;
                if (count > 6) return;
                Directory.CreateDirectory(_directory);
                var report = count == 6 ? "Further identical reports are suppressed for this session.\n" :
                    $"\nCRASH REPORT - {DateTime.Now:yyyy-MM-dd HH:mm:ss}\nApp Version: {_version}\nSource: {source}\nOccurrence: {count}\n{ex}\n";
                File.AppendAllText(Path.Combine(_directory, "crash.log"), LogRedactor.Redact(report));
            }
        }
        catch { }
    }
}
