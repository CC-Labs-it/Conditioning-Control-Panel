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
internal sealed class LinuxRaceHost : IDisposable
{
    internal static LinuxRaceHost? Current { get; private set; }
    private readonly Process _process;
    private readonly Action<DateTime> _panic;
    private readonly int _pid;
    private readonly GlassroadSession _session;
    private readonly DispatcherTimer _watch;
    private readonly object _writeLock = new();
    private Task _writes = Task.CompletedTask;
    private int _closed;
    private long _lastEscape;
    internal bool InFront { get; private set; }
    internal bool Ready => _session.Ready;
    internal static string EnginePath => Path.Combine(CorePaths.UserData, "engines", "electron-45.1.0", "electron");
    internal static string ShellPath => Path.Combine(AppContext.BaseDirectory, "Resources", "web", "glassroad-shell", "main.js");
    internal static bool TryLaunch(Uri url)
    {
        if (!OperatingSystem.IsLinux()) return false;
        if (Current is { } live) { live.Post(new { type = "focus" }); return true; }
        if (!File.Exists(EnginePath) || !File.Exists(ShellPath)) return false;
        var start = new ProcessStartInfo("/usr/bin/setsid")
        {
            UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true,
            RedirectStandardError = true, CreateNoWindow = true
        };
        // setsid execs without forking: this child's pid is its process group, never CCP's group.
        start.ArgumentList.Add(EnginePath);
        start.ArgumentList.Add(ShellPath);
        start.ArgumentList.Add(url.AbsoluteUri);
        var process = new Process { StartInfo = start };
        try
        {
            if (!process.Start()) throw new IOException("Race shell did not start");
            Current = new LinuxRaceHost(process);
            Current.StartReading();
            return true;
        }
        catch
        {
            try { kill(process.Id, 9); kill(-process.Id, 9); } catch { }
            process.Dispose();
            throw; // no silent renderer fallback on a startup failure
        }
    }
    internal LinuxRaceHost(Process process, Action<DateTime>? panic = null)
    {
        _process = process; _pid = process.Id;
        _panic = panic ?? (now => MainShellWindow.Current?.HandlePanicKeyPress(now));
        _session = new GlassroadSession(Post, Dispose, () => Post(new { type = "focus" }));
        _watch = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _watch.Tick += (_, _) => _session.CheckGlassroadWatch(DateTime.UtcNow);
        _watch.Start(); Current = this;
    }
    internal void StartReading()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var line = new System.Text.StringBuilder();
                var buffer = new char[1];
                while (await _process.StandardOutput.ReadAsync(buffer, 0, 1) != 0)
                {
                    if (buffer[0] == '\n')
                    {
                        var frame = RaceShellProtocol.Parse(line.ToString()); line.Clear();
                        Dispatcher.UIThread.Post(() => Receive(frame));
                    }
                    else if (line.Length >= RaceShellProtocol.MaxLineLength) break;
                    else line.Append(buffer[0]);
                }
            }
            catch { }
            finally { Kill(); Dispatcher.UIThread.Post(Dispose); }
        });
        _ = Task.Run(async () => { try { var b = new char[1024]; while (await _process.StandardError.ReadAsync(b, 0, b.Length) != 0) { } } catch { } });
    }
    internal void Receive(JObject frame)
    {
        if (Volatile.Read(ref _closed) != 0) return;
        switch ((string?)frame["type"])
        {
            case RaceShellProtocol.Loaded: _session.PostGlassroadInit(); break;
            case RaceShellProtocol.Focus: InFront = (bool?)frame["active"] == true; break;
            case RaceShellProtocol.Escape:
                if (!RaceShellProtocol.AcceptEscape(ref _lastEscape, (long?)frame["sequence"] ?? 0,
                    Platform.X11PanicKey.IsListening && Platform.X11PanicKey.BoundKeycode != 0 && CoreSettings.Current.PanicKey == "Escape")) break;
                InFront = true;
                if (CoreSettings.Current.PanicKey == "Escape") _panic(DateTime.Now);
                else Pause();
                break;
            case "boot-error": Dispose(); break;
            default: _session.HandleGlassroad(frame); break;
        }
    }
    internal void Pause() => Post(new { type = "kept-escape" });
    private void Post(object frame)
    {
        if (Volatile.Read(ref _closed) != 0) return;
        var json = JObject.FromObject(frame);
        if ((string?)json["type"] == "init")
        {
            json["caps"]!["forceRenderer"] = "webgl2";
            json["caps"]!["scrolller"] = false;
        }
        lock (_writeLock) _writes = _writes.ContinueWith(_ =>
        {
            try { if (_closed == 0) { _process.StandardInput.WriteLine(json.ToString(Formatting.None)); _process.StandardInput.Flush(); } }
            catch { Kill(); Dispatcher.UIThread.Post(Dispose); }
        });
    }
    [DllImport("libc", SetLastError = true)] private static extern int kill(int pid, int signal);
    internal void Kill()
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0) return;
        // Exact child first, then its isolated group, including orphaned Chromium helpers.
        kill(_pid, 9);
        kill(-_pid, 9);
    }
    public void Dispose()
    {
        Kill(); _watch.Stop(); _session.Dispose();
        if (ReferenceEquals(Current, this)) Current = null;
        _process.Dispose();
    }
}
