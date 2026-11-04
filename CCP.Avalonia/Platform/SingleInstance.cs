using System;
using System.IO;
using System.IO.Pipes;
using System.Buffers.Binary;
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
    /// one length byte for short payloads, or 255 and a four-byte length for long UTF-8 payloads; 0 is bare.
    /// That framing and file requests use the "_v2" pipe. The original pipe keeps the one-byte framing, so
    /// older builds (which route any payload as a surface) only ever exchange surface payloads with this one.
    /// </summary>
    internal sealed class SingleInstance : IDisposable
    {
        public const string MutexName = "ConditioningControlPanel_SingleInstance_Mutex";
        internal const int MaxPayloadBytes = 131072;
        internal const string V2Suffix = "_v2";
        const int LegacyFallbackAfterMs = 2000;
        const int ShowAckTimeoutMs = 10000;                          // WPF App.xaml.cs:68
        static readonly TimeSpan LegacyTakeoverWait = TimeSpan.FromSeconds(8);   // WPF App.xaml.cs:1862
        static readonly TimeSpan StaleTakeoverWait = TimeSpan.FromSeconds(3);    // WPF App.xaml.cs:1914

        readonly Mutex _mutex;
        readonly bool _owned;
        readonly CancellationTokenSource _cts = new();

        SingleInstance(Mutex mutex, bool owned, string pipe, Func<string?, Task> show)
        {
            _mutex = mutex;
            _owned = owned;
            // Only the mutex owner answers; an unowned run (wedged primary) would fight it for the pipe.
            if (!owned) return;
            // v2 is created first, so a sender reaching only the original pipe has found an older build.
            var current = TryCreateServer(pipe + V2Suffix);
            var legacy = TryCreateServer(pipe);
            _ = Task.Run(() => ListenAsync(pipe + V2Suffix, current, true, show, _cts.Token));
            _ = Task.Run(() => ListenAsync(pipe, legacy, false, show, _cts.Token));
        }

        internal static string PipeName(string suffix) => "ConditioningControlPanel_ShowWindow_Signal_" + Environment.UserName + suffix;

        /// <summary>A sandboxed profile (CCP_USERDATA_DIR) is a different instance, so kc and tests never
        /// wake or block the user's real app. A shipped build never sets it and holds WPF's exact name.</summary>
        public static string SandboxSuffix()
        {
            var dir = Environment.GetEnvironmentVariable("CCP_USERDATA_DIR");
            return string.IsNullOrEmpty(dir) ? "" : "_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(dir)))[..12];
        }

        /// <summary>Null means a live primary acknowledged the show request and this launch must exit.</summary>
        public static SingleInstance? Claim(string suffix, Func<string?, Task> show, string? payload = null)
            => Claim(suffix, show, payload, payload);

        /// <summary><paramref name="legacyPayload"/> is what an older primary receives in place of <paramref name="payload"/>.</summary>
        public static SingleInstance? Claim(string suffix, Func<string?, Task> show, string? payload, string? legacyPayload)
        {
            var pipe = PipeName(suffix);
            var mutex = CreateMutex(MutexName + suffix, out bool owned);
            if (!owned)
            {
                bool? acked = RequestShow(pipe, payload, legacyPayload);
                if (acked == true) { mutex.Dispose(); return null; }
                // No listener: a primary still exiting mid-update (#466) - wait for it, else exit (WPF 1861-1870).
                // Listener but no ack: wedged primary. ponytail: WPF also kills it (KillStaleInstances);
                // here this launch runs without the mutex after the short wait, as WPF does when the kill fails.
                try { owned = mutex.WaitOne(acked == null ? LegacyTakeoverWait : StaleTakeoverWait); }
                catch (AbandonedMutexException) { owned = true; }
                if (!owned && acked == null) { mutex.Dispose(); return null; }
            }
            return new SingleInstance(mutex, owned, pipe, show);
        }

        /// <summary>The options of the instance mutex on Unix, null on Windows. Windows keeps WPF's plain
        /// name (session-local there, which is what the installer's AppMutex looks for). On Unix a plain name
        /// is scoped to the Unix SESSION, so an autostart launch and a menu launch (different sessions, or a
        /// <c>setsid</c> start) each got their own mutex and two instances ran on one profile (review R4).
        /// Per user across every session: one app per user, never blocked by another user's.</summary>
        internal static NamedWaitHandleOptions? UnixMutexOptions() =>
            OperatingSystem.IsWindows() ? null : new NamedWaitHandleOptions { CurrentUserOnly = true, CurrentSessionOnly = false };

        internal static Mutex CreateMutex(string name, out bool owned) =>
            UnixMutexOptions() is { } options
                ? new Mutex(true, name, options, out owned)
                : new Mutex(true, name, out owned);

        /// <summary>true = acked, false = connected but no ack in time, null = nobody listening.</summary>
        static bool? RequestShow(string pipe, string? payload, string? legacyPayload)
        {
            var bytes = Encoding.UTF8.GetBytes(payload ?? "");
            if (bytes.Length > MaxPayloadBytes) return null;
            // Serialize senders across processes. Unix pipe connect can succeed just as another
            // connected server closes, so competing clients otherwise lose their request.
            using var gate = CreateMutex(pipe + "_send", out bool ownsGate);
            try
            {
                if (!ownsGate)
                {
                    try { ownsGate = gate.WaitOne(ShowAckTimeoutMs); }
                    catch (AbandonedMutexException) { ownsGate = true; }
                }
                if (!ownsGate) return null;
                using var c = ConnectPipe(pipe, out bool current);
                if (!current)
                {
                    bytes = Encoding.UTF8.GetBytes(legacyPayload ?? "");
                    if (bytes.Length > byte.MaxValue) bytes = Array.Empty<byte>();   // unreadable reads as a bare relaunch
                    c.WriteByte((byte)bytes.Length);
                }
                else if (bytes.Length < byte.MaxValue) c.WriteByte((byte)bytes.Length);
                else
                {
                    c.WriteByte(byte.MaxValue);
                    var extended = new byte[4];
                    BinaryPrimitives.WriteInt32LittleEndian(extended, bytes.Length);
                    c.Write(extended);
                }
                c.Write(bytes);
                c.Flush();
                using var cts = new CancellationTokenSource(ShowAckTimeoutMs);
                var buf = new byte[1];
                return c.ReadAsync(buf, cts.Token).AsTask().GetAwaiter().GetResult() == 1;
            }
            catch (TimeoutException) { return null; }
            catch (OperationCanceledException) { return false; }
            catch (Exception ex) { Serilog.Log.Warning(ex, "Single-instance show request failed"); return null; }
            finally { if (ownsGate) gate.ReleaseMutex(); }
        }

        // Prefer v2; only after a grace period with v2 unreachable accept the original pipe of an older build.
        static NamedPipeClientStream ConnectPipe(string pipe, out bool current)
        {
            var start = Environment.TickCount64;
            while (true)
            {
                var elapsed = Environment.TickCount64 - start;
                if (TryConnect(pipe + V2Suffix) is { } v2) { current = true; return v2; }
                if (elapsed >= LegacyFallbackAfterMs && TryConnect(pipe) is { } legacy) { current = false; return legacy; }
                if (elapsed >= ShowAckTimeoutMs) throw new TimeoutException("No single-instance listener");
                Thread.Sleep(25);
            }
        }

        static NamedPipeClientStream? TryConnect(string pipe)
        {
            var client = new NamedPipeClientStream(".", pipe, PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            try
            {
                client.Connect(250); return client;
            }
            catch (Exception ex) when (ex is TimeoutException or IOException)
            {
                client.Dispose();
                return null;
            }
        }

        static NamedPipeServerStream CreateServer(string pipe) =>
            new(pipe, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

        static NamedPipeServerStream? TryCreateServer(string pipe)
        {
            try { return CreateServer(pipe); }
            catch (Exception ex) { Serilog.Log.Warning(ex, "Single-instance listener"); return null; }
        }

        static async Task ListenAsync(string pipe, NamedPipeServerStream? first, bool extended, Func<string?, Task> show, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                var server = first;
                first = null;
                try
                {
                    server ??= CreateServer(pipe);
                    await server.WaitForConnectionAsync(ct);
                    _ = HandleConnectionAsync(server, extended, show, ct);
                    server = null; // the handler owns it
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
                catch (Exception ex)
                {
                    Serilog.Log.Warning(ex, "Single-instance listener");
                    try { await Task.Delay(1000, ct); } catch (OperationCanceledException) { return; }
                }
                finally { server?.Dispose(); }
            }
            first?.Dispose();   // cancelled before the first wait
        }

        static async Task HandleConnectionAsync(NamedPipeServerStream server, bool extended, Func<string?, Task> show, CancellationToken ct)
        {
            await using (server)
            {
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    timeout.CancelAfter(ShowAckTimeoutMs);
                    var payload = await ReadPayloadAsync(server, timeout.Token, extended);
                    // Ack after the request is accepted, even if showing it failed (WPF App.xaml.cs:1985).
                    try { await show(payload); } catch (Exception ex) { Serilog.Log.Warning(ex, "Single-instance show failed"); }
                    await server.WriteAsync(new byte[] { 1 }, timeout.Token);
                    await server.FlushAsync(timeout.Token);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
                catch (Exception ex) { Serilog.Log.Warning(ex, "Single-instance request failed"); }
            }
        }

        internal static async Task<string?> ReadPayloadAsync(Stream stream, CancellationToken ct, bool extended = true)
        {
            var len = new byte[1];
            await stream.ReadExactlyAsync(len, ct);
            int count = len[0];
            if (extended && count == byte.MaxValue)
            {
                var wide = new byte[4];
                await stream.ReadExactlyAsync(wide, ct);
                count = BinaryPrimitives.ReadInt32LittleEndian(wide);
                if (count < byte.MaxValue || count > MaxPayloadBytes) throw new InvalidDataException("Invalid handoff length");
            }
            var body = new byte[count];
            await stream.ReadExactlyAsync(body, ct);
            return count == 0 ? null : new UTF8Encoding(false, true).GetString(body);
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
