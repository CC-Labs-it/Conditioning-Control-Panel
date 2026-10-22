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
        public const long Gb = LocalAiHardwareRules.Gb;
        public const long RecommendedRamBytes = LocalAiHardwareRules.RecommendedRamBytes;
        public const long RecommendedVramBytes = LocalAiHardwareRules.RecommendedVramBytes;
        public static LocalAiHardwareRules.Fit Judge(long ramBytes, long vramBytes)
            => LocalAiHardwareRules.Judge(ramBytes, vramBytes);
        public static string FormatGb(long bytes) => LocalAiHardwareRules.FormatGb(bytes);

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
