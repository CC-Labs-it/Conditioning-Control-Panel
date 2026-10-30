using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace ConditioningControlPanel.Avalonia.Platform
{
    internal static class LocalAiHardware
    {
        public static long TotalRamBytes()
        {
            if (OperatingSystem.IsWindows()) return WindowsRamBytes();
            try
            {
                if (OperatingSystem.IsLinux())
                    foreach (var line in File.ReadLines("/proc/meminfo"))
                        if (line.StartsWith("MemTotal:", StringComparison.Ordinal))
                            return long.Parse(line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[1]) * 1024;
            }
            catch { }
            return 0;
        }

        public static long LargestVramBytes()
        {
            if (OperatingSystem.IsWindows()) return WindowsVramBytes();
            long best = 0;
            try
            {
                if (OperatingSystem.IsLinux())
                    foreach (var card in Directory.EnumerateDirectories("/sys/class/drm", "card*"))
                        if (long.TryParse(File.Exists(Path.Combine(card, "device/mem_info_vram_total"))
                            ? File.ReadAllText(Path.Combine(card, "device/mem_info_vram_total")) : null, out var size))
                            best = Math.Max(best, size);
            }
            catch { }
            return best;
        }

        // ---- live reads ----

        private static long WindowsRamBytes()
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
        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private static long WindowsVramBytes()
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
