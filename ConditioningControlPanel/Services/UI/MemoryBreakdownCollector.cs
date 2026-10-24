using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ConditioningControlPanel.Services;

/// <summary>
/// Walks this process's address space and says where the committed memory sits: MEM_PRIVATE /
/// MEM_MAPPED / MEM_IMAGE totals, the biggest private allocations (sizes only), native heaps via
/// HeapSummary and the GC's own commit. Feeds <see cref="MemoryJumpRule.FormatJumpLine"/>.
///
/// Never throws (diagnostics must not take the app down), reads no memory contents, and stops at
/// <see cref="BudgetMs"/> with a partial result. Callers run it off the UI thread.
/// </summary>
internal static class MemoryBreakdownCollector
{
    internal const int BudgetMs = 200;
    private const int TopRegions = 8;
    private const int MaxHeaps = 512;
    private const double MB = 1024.0 * 1024.0;

    private const uint MEM_COMMIT = 0x1000;
    private const uint MEM_PRIVATE = 0x20000;
    private const uint MEM_MAPPED = 0x40000;
    private const uint MEM_IMAGE = 0x1000000;

    internal static MemoryBreakdown Collect()
    {
        var sw = Stopwatch.StartNew();
        double priv = 0, mapped = 0, image = 0;
        var top = new List<double>(TopRegions + 1);
        bool partial = false;

        try
        {
            int size = Marshal.SizeOf<MEMORY_BASIC_INFORMATION>();
            ulong addr = 0;
            IntPtr curBase = IntPtr.Zero;
            ulong curCommitted = 0;
            int n = 0;
            while (true)
            {
                if ((++n & 0xFF) == 0 && sw.ElapsedMilliseconds > BudgetMs) { partial = true; break; }
                if (VirtualQuery((IntPtr)(long)addr, out var mbi, (UIntPtr)size) == UIntPtr.Zero) break;
                ulong region = (ulong)mbi.RegionSize;
                if (region == 0) break;

                if (mbi.State == MEM_COMMIT)
                {
                    if (mbi.Type == MEM_PRIVATE)
                    {
                        priv += region;
                        // One VirtualAlloc reservation can come back as several regions (mixed
                        // protection); sum by allocation base so a 32 MB buffer reads as 32.
                        if (mbi.AllocationBase != curBase)
                        {
                            PushTop(top, curCommitted);
                            curBase = mbi.AllocationBase;
                            curCommitted = 0;
                        }
                        curCommitted += region;
                    }
                    else if (mbi.Type == MEM_MAPPED) mapped += region;
                    else if (mbi.Type == MEM_IMAGE) image += region;
                }

                ulong next = (ulong)(long)mbi.BaseAddress + region;
                if (next <= addr) break;
                addr = next;
            }
            PushTop(top, curCommitted);
        }
        catch { partial = true; }

        double heaps = -1;
        int heapCount = 0;
        try { heaps = SumHeaps(out heapCount); } catch { heaps = -1; }

        double gc = 0;
        // TotalCommittedBytes is as of the last GC: before the first one it reads 0, so fall back to the heap size.
        try
        {
            long committed = GC.GetGCMemoryInfo().TotalCommittedBytes;
            gc = (committed > 0 ? committed : GC.GetTotalMemory(false)) / MB;
        }
        catch { }

        top.Sort((a, b) => b.CompareTo(a));
        var topMB = top.ConvertAll(b => b / MB);
        return new MemoryBreakdown(priv / MB, mapped / MB, image / MB, heaps, heapCount, gc,
            topMB, sw.ElapsedMilliseconds, partial);
    }

    private static void PushTop(List<double> top, ulong committed)
    {
        if (committed == 0) return;
        double v = committed;
        if (top.Count < TopRegions) { top.Add(v); return; }
        int min = 0;
        for (int i = 1; i < top.Count; i++) if (top[i] < top[min]) min = i;
        if (v > top[min]) top[min] = v;
    }

    /// <summary>Committed MB over every native heap, or -1 when the heap list cannot be read.</summary>
    private static double SumHeaps(out int count)
    {
        count = 0;
        uint want = GetProcessHeaps(0, null);
        if (want == 0) return -1;
        var handles = new IntPtr[Math.Min(want, (uint)MaxHeaps)];
        uint got = GetProcessHeaps((uint)handles.Length, handles);
        if (got == 0) return -1;
        count = (int)Math.Min(got, (uint)handles.Length);

        double sum = 0;
        for (int i = 0; i < count; i++)
        {
            var hs = new HEAP_SUMMARY { cb = (uint)Marshal.SizeOf<HEAP_SUMMARY>() };
            if (HeapSummary(handles[i], 0, ref hs)) sum += (ulong)hs.cbCommitted;
        }
        return sum / MB;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORY_BASIC_INFORMATION
    {
        public IntPtr BaseAddress;
        public IntPtr AllocationBase;
        public uint AllocationProtect;
        public ushort PartitionId;
        public UIntPtr RegionSize;
        public uint State;
        public uint Protect;
        public uint Type;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HEAP_SUMMARY
    {
        public uint cb;
        public UIntPtr cbAllocated;
        public UIntPtr cbCommitted;
        public UIntPtr cbReserved;
        public UIntPtr cbMaxReserve;
    }

    [DllImport("kernel32.dll")]
    private static extern UIntPtr VirtualQuery(IntPtr lpAddress, out MEMORY_BASIC_INFORMATION lpBuffer, UIntPtr dwLength);

    [DllImport("kernel32.dll")]
    private static extern uint GetProcessHeaps(uint numberOfHeaps, [Out] IntPtr[]? processHeaps);

    [DllImport("kernel32.dll")]
    private static extern bool HeapSummary(IntPtr hHeap, uint dwFlags, ref HEAP_SUMMARY lpSummary);
}
