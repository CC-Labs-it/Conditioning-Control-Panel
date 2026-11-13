using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Race;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
namespace ConditioningControlPanel.Avalonia.Views.Games;
/// <summary>B2 (#3702) step 3: the owned Electron race shell, one process group per race. Its GameWindow is never shown
/// and keeps the whole Glassroad session (GameWindow.Glassroad.cs); this class only owns the process and its pipe.</summary>
internal sealed class LinuxRaceHost : IDisposable
{
    internal static LinuxRaceHost? Current { get; private set; }
    internal static Func<MainShellWindow?> Shell = () => MainShellWindow.Current;
    private readonly GameWindow _window;
    private readonly Process _process;
    private readonly Action<DateTime> _panic;
    private readonly int _pid;
    private readonly DispatcherTimer _watch;
    private readonly object _writeLock = new();
    private Task _writes = Task.CompletedTask;
    private int _closed; private long _lastEscape; private bool _escapeHeld, _disposed;
    private volatile string? _displayBackend; private volatile bool _backendKnown, _loaded;
    private readonly Func<bool> _x11Listening; private readonly string? _launchBackend;   // the session's ozone platform; null = unknown
    internal bool InFront { get; private set; }
    internal bool IsAlive => Volatile.Read(ref _closed) == 0;
    /// <summary>The race session window while the live shell has focus (TryGamePauseOnEscape's front game).</summary>
    internal static GameWindow? InFrontWindow => Current is { InFront: true, IsAlive: true } host ? host._window : null;
    internal static string? EnginePath => RuntimeInformation.ProcessArchitecture is Architecture.X64 or Architecture.Arm64
        ? Path.Combine(RaceEngineFetcher.DefaultRoot, RaceEngineManifest.For(RuntimeInformation.ProcessArchitecture).DirectoryName, "electron") : null;
    internal static string ShellPath => Path.Combine(AppContext.BaseDirectory, "Resources", "web", "glassroad-shell", "main.js");
    private const string LaunchBackend = "x11"; internal static LinuxRaceHost? TryLaunch(GameWindow window, Uri url) => TryLaunch(window, url, EnginePath, ShellPath);
    /// <summary>Null when the engine or shell is not installed; throws when they are and the shell did not start.</summary>
    internal static LinuxRaceHost? TryLaunch(GameWindow window, Uri url, string? enginePath, string shellPath)
    {
        if (!OperatingSystem.IsLinux() || enginePath == null || !File.Exists(enginePath) || !File.Exists(shellPath)) return null;
        var start = new ProcessStartInfo("/usr/bin/setsid") { UseShellExecute = false, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        // setsid execs without forking: this child's pid is its process group, never CCP's group.
        foreach (var arg in new[] { enginePath, "--ozone-platform=" + LaunchBackend, shellPath }) start.ArgumentList.Add(arg);
        var process = new Process { StartInfo = start };
        LinuxRaceHost? host = null;
        try
        {
            if (!process.Start()) throw new IOException("Race shell did not start");
            host = new LinuxRaceHost(window, process, launchBackend: LaunchBackend);
            host.Send(new { type = "launch", url = url.AbsoluteUri }); return host.StartReading();
        }
        catch
        {
            if (host != null) { host.Kill(); host._watch.Stop(); Current = null; }
            else try { kill(process.Id, 9); kill(-process.Id, 9); } catch { }
            process.Dispose();
            throw; // the caller shows the race card with rt_engine_start_failed
        }
    }
    internal LinuxRaceHost(GameWindow window, Process process, Action<DateTime>? panic = null, Func<bool>? x11Listening = null, string? launchBackend = null)
    {
        _window = window; _process = process; _pid = process.Id; _launchBackend = launchBackend;
        _x11Listening = x11Listening ?? (() => Platform.X11PanicKey.IsListening && Platform.X11PanicKey.BoundKeycode != 0);
        _panic = panic ?? (now => Shell()?.HandlePanicKeyPress(now));
        _watch = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _watch.Tick += (_, _) => CheckPanicKey();
        _watch.Start(); Current = this;
    }
    internal LinuxRaceHost StartReading()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var line = new System.Text.StringBuilder(); var buffer = new char[1];
                while (await _process.StandardOutput.ReadAsync(buffer, 0, 1) != 0)
                {
                    if (buffer[0] == '\n') { var frame = RaceShellProtocol.Parse(line.ToString()); line.Clear(); ReadFrame(frame); }
                    else if (line.Length >= RaceShellProtocol.MaxLineLength) break;
                    else line.Append(buffer[0]);
                }
            }
            catch { }
            finally { Kill(pipeEnded: true); Dispatcher.UIThread.Post(Dispose); }
        });
        _ = Task.Run(async () => { try { var b = new char[1024]; while (await _process.StandardError.ReadAsync(b, 0, b.Length) != 0) { } } catch { } });
        return this;
    }
    internal void CheckPanicKey() { if (UnsupportedPanicKey) Dispose(); }
    // A non-Escape key needs X11 for panic or leash safety (WPF MainWindow.Leash.cs:402-409, 432-437).
    private bool UnsupportedPanicKey => _backendKnown && MainShellWindow.PanicRouteWanted && CoreSettings.Current.PanicKey != "Escape" && !(_displayBackend == "x11" && _x11Listening());
    // One panic source per session, never both: XInput2 raw events reach the listener AND an X11 shell, on separate
    // channels, so no time window can pair them. The launch fixes the backend; a page reporting another hands it to the shell.
    private bool X11Source => _launchBackend == "x11" && (_displayBackend is null or "x11") && _x11Listening() && CoreSettings.Current.PanicKey == "Escape";
    // Runs on the pipe reader: arm the watchdog before anything is queued behind a wedged UI.
    internal void ReadFrame(JObject frame)
    {
        if (!IsAlive) return;
        var type = (string?)frame["type"];
        if (type == RaceShellProtocol.Loaded)
        {
            _displayBackend = (string?)frame["displayBackend"]; _backendKnown = true; _loaded = true;
            if (UnsupportedPanicKey) { Kill(); Dispatcher.UIThread.Post(Dispose); return; }
        }
        if (type != RaceShellProtocol.Escape) { Dispatcher.UIThread.Post(() => Receive(frame)); return; }
        bool x11 = X11Source;
        if (!RaceShellProtocol.AcceptEscape(ref _lastEscape, (long?)frame["sequence"] ?? 0, x11))
            { if (x11) Serilog.Log.Debug("Glassroad: shell Escape ignored, X11 is this race's panic source"); return; }
        void Handle()
        {
            bool escapePanics = CoreSettings.Current.PanicKey == "Escape";
            // Killed meanwhile (watchdog): the race is gone, so the recovered UI still runs the full panic, not a pause.
            if (!IsAlive) { try { if (escapePanics) _panic(DateTime.Now); } finally { Dispose(); } return; }
            InFront = true; if (!escapePanics) { Pause(); return; }
            // WPF hold-to-cut rides the panic key (OnX11PanicPress): this shell owns it, so it reports the release.
            _panic(DateTime.Now); Shell()?.OnRaceEscape(true); _escapeHeld = IsAlive;
            if (!_escapeHeld) Shell()?.OnRaceEscape(null);
        }
        if (CoreSettings.Current.PanicKey == "Escape" && CoreSettings.Current.PanicKeyEnabled && !MainShellWindow.LockdownActive
            && !MainShellWindow.CapturingPanicKey && !Controls.AppSettings.DevicesSettingsSection.CapturingPauseKey)
            Platform.PanicWatchdog.QueueWatched(Handle);
        else Dispatcher.UIThread.Post(Handle);
    }
    internal void Receive(JObject frame)
    {
        if (!IsAlive) return;
        var type = (string?)frame["type"];
        switch (type)
        {
            case RaceShellProtocol.Loaded:
                _displayBackend = (string?)frame["displayBackend"]; _backendKnown = true; _loaded = true;
                if (UnsupportedPanicKey) { Dispose(); break; }
                _window.PostGlassroadInit(); break;
            case RaceShellProtocol.Focus:
                InFront = (bool?)frame["active"] == true;
                // A lost key-up never asks (LeashHold.cs): focus gone mid-hold hands off to the key probe.
                if (!InFront && _escapeHeld) { _escapeHeld = false; Shell()?.OnRaceEscape(null); }
                break;
            case RaceShellProtocol.Escape: ReadFrame(frame); break;
            case RaceShellProtocol.EscapeUp: if (_escapeHeld) { _escapeHeld = false; Shell()?.OnRaceEscape(false); } break;
            default:
                _window.HandleMessage(frame.ToString(Formatting.None));   // the window's own Glassroad and shell frames
                if (type == "ready") Focus();
                break;
        }
    }
    internal void Pause() => _window.PostKeptEscape();
    internal void Focus() => Send(new { type = "focus" });
    internal void Send(object frame)
    {
        if (!IsAlive) return;
        var json = frame is string s ? JObject.Parse(s) : JObject.FromObject(frame);
        if ((string?)json["type"] == "init" && json["caps"] is JObject caps) caps["forceRenderer"] = "webgl2";
        lock (_writeLock) _writes = _writes.ContinueWith(_ =>
        {
            try { if (IsAlive) { _process.StandardInput.WriteLine(json.ToString(Formatting.None)); _process.StandardInput.Flush(); } }
            catch { Kill(pipeEnded: true); Dispatcher.UIThread.Post(Dispose); }
        });
    }
    [DllImport("libc", SetLastError = true)] private static extern int kill(int pid, int signal);
    /// <summary>Thread-safe (the panic watchdog calls it off the UI thread): the race is gone, never in front again.</summary>
    /// <summary>The first kill sets the one end state: a pipe ended before any page loaded is a failed start (B2 section 1).</summary>
    internal void Kill(bool pipeEnded = false)
    {
        InFront = false;
        if (Interlocked.CompareExchange(ref _closed, pipeEnded && !_loaded ? 2 : 1, 0) != 0) return;
        // Exact child first, then its isolated group, including orphaned Chromium helpers.
        kill(_pid, 9); kill(-_pid, 9);
    }
    public void Dispose()
    {
        Kill();
        if (_disposed) return;
        _disposed = true; _watch.Stop();
        if (ReferenceEquals(Current, this)) Current = null;
        _process.Dispose();
        if (_escapeHeld) { _escapeHeld = false; Shell()?.OnRaceEscape(null); }
        try { if (Volatile.Read(ref _closed) == 2) _window.RaceShellFailed(); else _window.Close(); }
        catch (Exception ex) { Serilog.Log.Debug(ex, "Glassroad: race window close failed"); }
    }
}
