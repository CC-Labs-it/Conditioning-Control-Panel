using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;

namespace ConditioningControlPanel.Services.Diagnostics
{
    /// <summary>GC counts per generation and the total GC pause, taken at one moment.</summary>
    internal readonly record struct GcMark(int Gen0, int Gen1, int Gen2, long PauseMs)
    {
        public static GcMark Now()
        {
            try
            {
                return new GcMark(GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2),
                    (long)GC.GetTotalPauseDuration().TotalMilliseconds);
            }
            catch { return default; }
        }
    }

    /// <summary>Machine-wide memory (GlobalMemoryStatusEx): load percent and the commit the process can still get.</summary>
    internal readonly record struct MemFacts(uint LoadPct, ulong CommitLimitBytes, ulong CommitAvailBytes, ulong PhysAvailBytes);

    /// <summary>
    /// Freeze hunt (7.4.2, handoff notes hunt.md B): in both freeze logs the heartbeat thread itself was silent
    /// for 8 to 11 s, so the stall was process-wide (a long GC or memory pressure are the lead suspects). Every
    /// stall line now carries the stuck dispatcher op, the GC work since the last good beat, private MB and the
    /// machine's commit, and a heartbeat that overslept says so. Formatting is pure, pinned by StallFactsTests;
    /// the readers never throw.
    /// </summary>
    internal static class StallFacts
    {
        /// <summary>A 1 s heartbeat sleep that took longer than this means the whole process stalled.</summary>
        public const long LateMs = 2500;

        public static bool IsLate(long sleptMs) => sleptMs > LateMs;

        public static string LateLine(long sleptMs, long expectedMs)
            => string.Create(CultureInfo.InvariantCulture,
                $"heartbeat thread late by {sleptMs - expectedMs}ms (slept {sleptMs}ms for {expectedMs}ms) - the whole process stalled");

        /// <summary>" | op=... gc0+.. gc1+.. gc2+.. gcPause+..ms privMB=.. sys load ..% commit avail ../.. MB phys avail .. MB".</summary>
        public static string Line(string? op, GcMark since, GcMark now, long privMb, MemFacts? mem)
        {
            var ci = CultureInfo.InvariantCulture;
            var s = string.Create(ci,
                $" | op={(string.IsNullOrEmpty(op) ? "?" : op)} gc0+{now.Gen0 - since.Gen0} gc1+{now.Gen1 - since.Gen1} gc2+{now.Gen2 - since.Gen2} gcPause+{Math.Max(0, now.PauseMs - since.PauseMs)}ms privMB={(privMb >= 0 ? privMb.ToString(ci) : "?")}");
            if (mem is { } m)
                s += string.Create(ci,
                    $" sys load {m.LoadPct}% commit avail {m.CommitAvailBytes / Mb}/{m.CommitLimitBytes / Mb} MB phys avail {m.PhysAvailBytes / Mb} MB");
            return s;
        }

        /// <summary>The live facts since <paramref name="since"/>. Safe on any thread.</summary>
        public static string Now(GcMark since)
        {
            string op;
            try { op = UiOpTracker.Describe(); } catch { op = "?"; }
            return Line(op, since, GcMark.Now(), PrivateMb(), ReadMemory());
        }

        public static long PrivateMb()
        {
            try { using var p = Process.GetCurrentProcess(); return p.PrivateMemorySize64 / (long)Mb; }
            catch { return -1; }
        }

        public static MemFacts? ReadMemory()
        {
            try
            {
                var st = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
                if (!GlobalMemoryStatusEx(ref st)) return null;
                return new MemFacts(st.dwMemoryLoad, st.ullTotalPageFile, st.ullAvailPageFile, st.ullAvailPhys);
            }
            catch { return null; }
        }

        private const ulong Mb = 1024 * 1024;

        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);
    }

    /// <summary>A Debug line that repeats every second is written only when its text changes, so the 4096-line
    /// flight recorder keeps minutes of history instead of seconds (hunt.md B7). Thread-safe.</summary>
    internal sealed class LogOnChange
    {
        private string? _last;

        /// <summary>True the first time and whenever <paramref name="value"/> differs from the last one seen.</summary>
        public bool Changed(string value) => !string.Equals(Interlocked.Exchange(ref _last, value), value, StringComparison.Ordinal);
    }
}
