using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ConditioningControlPanel.Avalonia.Platform
{
    /// <summary>
    /// WPF's single-instance gate (App.xaml.cs:1819-1922) on both OSes. The mutex name is WPF's, because the
    /// installer's AppMutex and InitializeSetup look for it. WPF's named EventWaitHandles do not exist on Unix,
    /// so the show request and its ack travel over a named pipe (a Unix socket on Linux) instead. The request
    /// carries WPF's surface handoff (LauncherHandoff payload, App.xaml.cs:1951) in place of its handoff file:
    /// one length byte, then the UTF-8 payload; length 0 is a bare relaunch.
    /// </summary>
    internal sealed class SingleInstance : IDisposable
    {
        public const string MutexName = "ConditioningControlPanel_SingleInstance_Mutex";
        internal static int ShowAckTimeoutMs = 10000;                // WPF App.xaml.cs:68; tests shorten it
        /// <summary>WPF KillStaleInstances (App.xaml.cs:4058); tests swap it so nothing real is killed.</summary>
        internal static Func<int> KillStale = KillStaleInstances;
        static readonly TimeSpan LegacyTakeoverWait = TimeSpan.FromSeconds(8);   // WPF App.xaml.cs:1862
        internal static TimeSpan StaleTakeoverWait = TimeSpan.FromSeconds(3);    // WPF App.xaml.cs:1914; tests shorten it

        readonly Mutex _mutex;
        readonly bool _owned;
        readonly CancellationTokenSource _cts = new();
        volatile bool _startupPhase = true;

        SingleInstance(Mutex mutex, bool owned, string pipe, Func<string?, Task> show)
        {
            _mutex = mutex;
            _owned = owned;
            // Only the mutex owner answers; an unowned run (wedged primary) would fight it for the pipe.
            if (owned) _ = Task.Run(() => ListenAsync(pipe, show, () => _startupPhase, _cts.Token));
        }

        /// <summary>WPF App.xaml.cs:3976-3978: called on the first dispatcher pump. Until then the listener acks
        /// before the show runs (WPF 2133-2142), so a relaunch during a slow cold start does not kill a healthy
        /// primary whose UI thread is not pumping yet; afterwards the ack waits for the show, so a wedged
        /// dispatcher reads as wedged again.</summary>
        public void EndStartupPhase() => _startupPhase = false;

        /// <summary>A sandboxed profile (CCP_USERDATA_DIR) is a different instance, so kc and tests never
        /// wake or block the user's real app. A shipped build never sets it and holds WPF's exact name.</summary>
        public static string SandboxSuffix()
        {
            var dir = Environment.GetEnvironmentVariable("CCP_USERDATA_DIR");
            return string.IsNullOrEmpty(dir) ? "" : "_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(dir)))[..12];
        }

        /// <summary>Null means a live primary acknowledged the show request and this launch must exit.</summary>
        public static SingleInstance? Claim(string suffix, Func<string?, Task> show, string? payload = null)
        {
            var pipe = "ConditioningControlPanel_ShowWindow_Signal_" + Environment.UserName + suffix;
            var mutex = new Mutex(true, MutexName + suffix, out bool owned);
            if (!owned)
            {
                bool? acked = RequestShow(pipe, payload);
                if (acked == true) { mutex.Dispose(); return null; }
                // No listener: a primary still exiting mid-update (#466) - wait for it, else exit (WPF 1861-1870).
                // Listener but no ack: wedged primary. Kill it and take over (WPF 2104); the kill fails closed,
                // so when it ends nothing this launch still runs without the mutex after the short wait (WPF 2097-2101).
                if (acked == false)
                {
                    int killed = 0;
                    try { killed = KillStale(); } catch (Exception ex) { Serilog.Log.Warning(ex, "[LIFECYCLE] Takeover failed"); }
                    Serilog.Log.Warning("[LIFECYCLE] Recovered from a stale instance that did not acknowledge the show request; killed {Killed}", killed);
                }
                try { owned = mutex.WaitOne(acked == null ? LegacyTakeoverWait : StaleTakeoverWait); }
                catch (AbandonedMutexException) { owned = true; }
                if (!owned && acked == null) { mutex.Dispose(); return null; }
            }
            return new SingleInstance(mutex, owned, pipe, show);
        }

        static StringComparison PathCmp => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        /// <summary>Only the app's own executable may kill: under `dotnet x.dll` or a testhost the same path is
        /// shared by unrelated processes (other apps, parallel test shards), so the takeover aborts.</summary>
        internal static bool IsAppExecutable(string? selfPath)
        {
            if (string.IsNullOrWhiteSpace(selfPath)) return false;
            var name = Path.GetFileName(selfPath.Trim());   // not GetFileNameWithoutExtension: "CCP.Avalonia" -> "CCP"
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
            return string.Equals(name, typeof(SingleInstance).Assembly.GetName().Name, PathCmp);
        }

        /// <summary>WPF's fail-closed match (App.xaml.cs:4041-4057): kill only a process whose image path is
        /// readable AND equals ours; an unreadable path is never a target.</summary>
        internal static bool IsSameBuild(string? selfPath, string? otherPath) =>
            Normalize(selfPath) is { } s && Normalize(otherPath) is { } o && string.Equals(s, o, PathCmp);

        static string? Normalize(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            try { return Path.GetFullPath(path.Trim()); } catch { return path.Trim(); }
        }

        // Linux answers from the kernel (/proc/<pid>/exe), like WPF's QueryFullProcessImageName; MainModule second.
        static string? ImagePath(Process proc)
        {
            try { if (OperatingSystem.IsLinux() && new FileInfo($"/proc/{proc.Id}/exe").LinkTarget is { } t) return t; }
            catch (Exception) { }
            try { return proc.MainModule?.FileName; } catch { return null; }
        }

        /// <summary>The mutex is per profile (SandboxSuffix), so the kill is too: a sandboxed run never kills the
        /// user's app or another sandbox, nor the other way round. Linux reads the target's /proc environ and skips
        /// it when unreadable; other OSes cannot read it, so only an unsandboxed run (WPF's case) may kill there.</summary>
        internal static bool SameProfile(string? ourDir, byte[]? theirEnviron, bool environReadable)
        {
            ourDir ??= "";
            if (!environReadable) return ourDir.Length == 0;
            if (theirEnviron == null) return false;
            const string key = "CCP_USERDATA_DIR=";
            string theirs = "";
            foreach (var entry in Encoding.UTF8.GetString(theirEnviron).Split('\0'))
                if (entry.StartsWith(key, StringComparison.Ordinal)) { theirs = entry[key.Length..]; break; }
            return string.Equals(theirs, ourDir, StringComparison.Ordinal);
        }

        /// <summary>WPF App.xaml.cs:4058-4113: terminate every OTHER process running our exact executable;
        /// returns how many were actually terminated. Each verdict is logged.</summary>
        static int KillStaleInstances()
        {
            int killed = 0;
            try
            {
                string? self = Normalize(Environment.ProcessPath);
                string? ourDir = Environment.GetEnvironmentVariable("CCP_USERDATA_DIR");
                if (!IsAppExecutable(self))
                {
                    Serilog.Log.Warning("[LIFECYCLE] Takeover aborted: we run {Self}, not the app executable, so no other process can be confirmed to be this build", self);
                    return 0;
                }
                using var me = Process.GetCurrentProcess();
                foreach (var proc in Process.GetProcessesByName(me.ProcessName))
                {
                    int id = -1;
                    try
                    {
                        id = proc.Id;
                        if (id == Environment.ProcessId) continue;
                        var other = ImagePath(proc);
                        if (!IsSameBuild(self, other))
                        {
                            Serilog.Log.Warning("[LIFECYCLE] Takeover skipped pid {Pid}: it runs {Other}, we run {Self}", id, other ?? "(unreadable)", self);
                            continue;
                        }
                        byte[]? environ = null;
                        if (OperatingSystem.IsLinux()) try { environ = File.ReadAllBytes($"/proc/{id}/environ"); } catch (Exception) { }
                        if (!SameProfile(ourDir, environ, OperatingSystem.IsLinux()))
                        {
                            Serilog.Log.Warning("[LIFECYCLE] Takeover skipped pid {Pid}: not provably the same profile (CCP_USERDATA_DIR)", id);
                            continue;
                        }
                        proc.Kill();
                        proc.WaitForExit(5000);
                        killed++;
                        Serilog.Log.Warning("[LIFECYCLE] Takeover killed pid {Pid} ({Path}) after it failed to acknowledge the show request", id, other);
                    }
                    catch (Exception ex) { Serilog.Log.Warning("[LIFECYCLE] Takeover could not act on pid {Pid}: {Error}", id, ex.Message); }
                    finally { proc.Dispose(); }
                }
            }
            catch (Exception ex) { Serilog.Log.Warning("[LIFECYCLE] Takeover could not enumerate processes: {Error}", ex.Message); }
            return killed;
        }

        /// <summary>true = acked, false = connected but no ack in time, null = nobody listening.</summary>
        static bool? RequestShow(string pipe, string? payload)
        {
            var bytes = Encoding.UTF8.GetBytes(payload ?? "");
            if (bytes.Length > byte.MaxValue) bytes = Array.Empty<byte>();   // unreadable reads as a bare relaunch
            try
            {
                using var c = new NamedPipeClientStream(".", pipe, PipeDirection.InOut,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                c.Connect(1000);
                c.WriteByte((byte)bytes.Length);
                c.Write(bytes);
                c.Flush();
                using var cts = new CancellationTokenSource(ShowAckTimeoutMs);
                var buf = new byte[1];
                return c.ReadAsync(buf, cts.Token).AsTask().GetAwaiter().GetResult() == 1;
            }
            catch (TimeoutException) { return null; }
            catch (OperationCanceledException) { return false; }
            catch (Exception ex) { Serilog.Log.Warning(ex, "Single-instance show request failed"); return null; }
        }

        static async Task ListenAsync(string pipe, Func<string?, Task> show, Func<bool> startupPhase, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await using var s = new NamedPipeServerStream(pipe, PipeDirection.InOut, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await s.WaitForConnectionAsync(ct);
                    var len = new byte[1];
                    await s.ReadExactlyAsync(len, ct);
                    var body = new byte[len[0]];
                    await s.ReadExactlyAsync(body, ct);
                    string? payload = body.Length == 0 ? null : Encoding.UTF8.GetString(body);
                    if (startupPhase())
                    {
                        // Starting, not wedged: ack now and let the show run when the dispatcher pumps.
                        await s.WriteAsync(new byte[] { 1 }, ct);
                        await s.FlushAsync(ct);
                        _ = Task.Run(() => show(payload)).ContinueWith(
                            t => Serilog.Log.Warning(t.Exception, "Single-instance show failed"),
                            TaskContinuationOptions.OnlyOnFaulted);
                        continue;
                    }
                    // Ack even when showing fails: WPF acks after the attempt (App.xaml.cs:1985), and a
                    // missing ack reads as "wedged" to the second launch.
                    try { await show(payload); } catch (Exception ex) { Serilog.Log.Warning(ex, "Single-instance show failed"); }
                    await s.WriteAsync(new byte[] { 1 }, ct);
                    await s.FlushAsync(ct);
                }
                catch (OperationCanceledException) { return; }
                catch (Exception ex)
                {
                    Serilog.Log.Warning(ex, "Single-instance listener");
                    try { await Task.Delay(1000, ct); } catch (OperationCanceledException) { return; }
                }
            }
        }

        /// <summary>Call on the thread that claimed: a mutex is released by its owning thread (WPF OnExit 6135).</summary>
        public void Dispose()
        {
            _cts.Cancel();
            if (_owned) try { _mutex.ReleaseMutex(); } catch (ApplicationException) { }
            _mutex.Dispose();
        }
    }
}
