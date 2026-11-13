using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>Lists the DXGI adapters (CreateDXGIFactory1 + EnumAdapters1 + GetDesc1) so Program can see a
/// GPU-less machine before Avalonia picks its renderer (RenderBudget.ChooseRendering). Raw vtable calls,
/// no unsafe context and no COM interop types; any failure returns an empty list (= Avalonia's default; the loop is RenderBudget.CollectAdapters).</summary>
[SupportedOSPlatform("windows")]
internal static class DxgiAdapters
{
    private const int EnumAdapters1Slot = 12;   // IUnknown 3 + IDXGIObject 4 + IDXGIFactory 5
    private const int GetDesc1Slot = 10;        // IUnknown 3 + IDXGIObject 4 + IDXGIAdapter 3
    private const int EFail = unchecked((int)0x80004005);
    private static readonly Guid IidFactory1 = new("770aae78-f26f-4dba-a829-253c83d1b387");

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DXGI_ADAPTER_DESC1
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public uint VendorId, DeviceId, SubSysId, Revision;
        public UIntPtr DedicatedVideoMemory, DedicatedSystemMemory, SharedSystemMemory;
        public uint LuidLow; public int LuidHigh;
        public uint Flags;
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int EnumAdapters1Fn(IntPtr self, uint index, out IntPtr adapter);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetDesc1Fn(IntPtr self, out DXGI_ADAPTER_DESC1 desc);

    [DllImport("dxgi.dll")] private static extern int CreateDXGIFactory1(in Guid riid, out IntPtr factory);

    private static T Slot<T>(IntPtr com, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(com), slot * IntPtr.Size));

    public static List<RenderBudget.DxgiAdapter> List()
    {
        IntPtr factory = IntPtr.Zero;
        try
        {
            if (CreateDXGIFactory1(IidFactory1, out factory) < 0 || factory == IntPtr.Zero) return new();
            var enumAdapters = Slot<EnumAdapters1Fn>(factory, EnumAdapters1Slot);
            int Read(uint i, out RenderBudget.DxgiAdapter a)
            {
                a = default;
                int hr = enumAdapters(factory, i, out var adapter);
                if (hr < 0) return hr;
                try
                {
                    hr = Slot<GetDesc1Fn>(adapter, GetDesc1Slot)(adapter, out var d);
                    if (hr < 0) return hr == RenderBudget.DxgiErrorNotFound ? EFail : hr;
                    a = new RenderBudget.DxgiAdapter(d.Description ?? "", d.VendorId, d.DeviceId, d.Flags);
                    return 0;
                }
                finally { Marshal.Release(adapter); }
            }
            return RenderBudget.CollectAdapters(Read);
        }
        catch (Exception ex) { Log.Warning(ex, "DXGI adapter enumeration failed; keeping Avalonia's default renderer"); return new(); }
        finally { if (factory != IntPtr.Zero) Marshal.Release(factory); }
    }
}
