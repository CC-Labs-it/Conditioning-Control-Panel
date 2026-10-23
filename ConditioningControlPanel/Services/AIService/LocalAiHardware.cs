using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace ConditioningControlPanel.Services.AIService
{
    /// <summary>
    /// How well this PC can carry a local AI model (owner, 2026-10-14: "many users find their
    /// midrange pc slowed down"). The setup wizard shows the result on its consent page so nobody
    /// installs Ollama without being told it runs on their own graphics card and memory.
    /// <para>Reads are best effort and never throw: an unknown figure reads as 0 and never blocks
    /// the install, it only changes what the card says.</para>
    /// </summary>
    public static class LocalAiHardware
    {
        public const long Gb = 1L << 30;
        public const long RecommendedRamBytes = 16 * Gb;
        public const long RecommendedVramBytes = 8 * Gb;

        public enum Fit
        {
            /// <summary>Nothing could be read.</summary>
            Unknown,
            /// <summary>Under the recommendation: expect the whole PC to slow down while it thinks.</summary>
            Below,
            /// <summary>At or over the recommendation (it still costs the PC while it thinks).</summary>
            Ok
        }

        /// <summary>
        /// The verdict. Windows reports a little less than the installed memory (a 16 GB machine
        /// reads about 15.8 GB), so each figure gets 10% slack. A missing graphics figure does not
        /// count against the PC; a missing memory figure means nothing is known.
        /// </summary>
        public static Fit Judge(long ramBytes, long vramBytes)
        {
            if (ramBytes > 0 && ramBytes < RecommendedRamBytes * 0.9) return Fit.Below;
            if (vramBytes > 0 && vramBytes < RecommendedVramBytes * 0.9) return Fit.Below;
            return ramBytes > 0 ? Fit.Ok : Fit.Unknown;
        }

        /// <summary>Whole gigabytes for the card ("16 GB"); "?" when unknown.</summary>
        public static string FormatGb(long bytes)
            => bytes > 0 ? Math.Round(bytes / (double)Gb).ToString("0") + " GB" : "?";

        // ---- live reads ----

        public static long TotalRamBytes()
        {
            try
            {
                var s = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
                return GlobalMemoryStatusEx(ref s) ? (long)Math.Min(s.ullTotalPhys, long.MaxValue) : 0;
            }
            catch { return 0; }
        }

        /// <summary>
        /// The largest graphics memory of any display adapter, from the driver's registry entry
        /// (HardwareInformation.qwMemorySize, 64-bit; WMI AdapterRAM caps at 4 GB and lies).
        /// </summary>
        public static long LargestVramBytes()
        {
            long best = 0;
            try
            {
                using var cls = Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
                if (cls == null) return 0;
                foreach (var name in cls.GetSubKeyNames())
                {
                    if (name.Length != 4 || !char.IsDigit(name[0])) continue;
                    try
                    {
                        using var k = cls.OpenSubKey(name);
                        var v = ReadSize(k?.GetValue("HardwareInformation.qwMemorySize"))
                                ?? ReadSize(k?.GetValue("HardwareInformation.MemorySize"));
                        if (v is long n && n > best) best = n;
                    }
                    catch { }
                }
            }
            catch { }
            return best;
        }

        private static long? ReadSize(object? value) => value switch
        {
            long l => l,
            int i => (uint)i,
            byte[] b when b.Length >= 8 => BitConverter.ToInt64(b, 0),
            byte[] b when b.Length >= 4 => BitConverter.ToUInt32(b, 0),
            _ => null
        };

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
}
