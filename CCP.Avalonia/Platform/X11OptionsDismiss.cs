using System;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>WPF EmiOptionsWindow.xaml.cs:608-692: scoped, non-consuming left/right and Escape
/// hooks. RECORD device events contain the click's root coordinates, even after dispatch delay.
/// Own connections, no grabs, no pointer query and no input history. X11/XWayland only.</summary>
internal sealed class X11OptionsDismiss : IDisposable
{
    private const string X11 = "libX11.so.6", Xtst = "libXtst.so.6";
    [StructLayout(LayoutKind.Sequential)] private struct Range8 { public byte First, Last; }
    [StructLayout(LayoutKind.Sequential)] private struct Range16 { public ushort First, Last; }
    [StructLayout(LayoutKind.Sequential)] private struct ExtRange { public Range8 Major; public Range16 Minor; }
    [StructLayout(LayoutKind.Sequential)] private struct RecordRange
    {
        public Range8 Requests, Replies;
        public ExtRange ExtRequests, ExtReplies;
        public Range8 Delivered, Device, Errors;
        public int ClientStarted, ClientDied;
    }
    [StructLayout(LayoutKind.Sequential)] private struct RecordData
    {
        public nuint IdBase, Time, Sequence;
        public int Category, Swapped;
        public IntPtr Data;
        public nuint Length; // in four-byte units, not bytes
    }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void RecordCallback(IntPtr closure, IntPtr data);
    [DllImport(X11)] private static extern IntPtr XOpenDisplay(IntPtr name);
    [DllImport(X11)] private static extern int XCloseDisplay(IntPtr display);
    [DllImport(X11)] private static extern int XFree(IntPtr data);
    [DllImport(X11)] private static extern int XSync(IntPtr display, bool discard);
    [DllImport(X11)] private static extern int XFlush(IntPtr display);
    [DllImport(X11)] private static extern byte XKeysymToKeycode(IntPtr display, nuint keysym);
    [DllImport(Xtst)] private static extern bool XRecordQueryVersion(IntPtr display, out int major, out int minor);
    [DllImport(Xtst)] private static extern IntPtr XRecordAllocRange();
    [DllImport(Xtst)] private static extern nuint XRecordCreateContext(IntPtr display, int flags, ref nuint clients, int count, ref IntPtr ranges, int rangeCount);
    [DllImport(Xtst)] private static extern bool XRecordEnableContextAsync(IntPtr display, nuint context, RecordCallback callback, IntPtr closure);
    [DllImport(Xtst)] private static extern void XRecordProcessReplies(IntPtr display);
    [DllImport(Xtst)] private static extern bool XRecordDisableContext(IntPtr display, nuint context);
    [DllImport(Xtst)] private static extern bool XRecordFreeContext(IntPtr display, nuint context);
    [DllImport(Xtst)] private static extern void XRecordFreeData(IntPtr data);

    [StructLayout(LayoutKind.Sequential)] private struct PollFd { public int Fd; public short Events, Returned; }
    [DllImport(X11)] private static extern int XConnectionNumber(IntPtr display);
    [DllImport("libc")] private static extern int poll(ref PollFd fd, nuint count, int timeout);

    private readonly Action<PixelPoint?, bool> _press;
    private int _escapeCode;
    private readonly RecordCallback _callback;
    private readonly object _gate = new();
    private IntPtr _control, _data;
    private nuint _context;
    private int _disposed;

    // Tests use the same wire-event decoder without opening desktop connections.
    internal X11OptionsDismiss(Action<PixelPoint?, bool> press, int escapeCode)
    {
        _press = press;
        _escapeCode = escapeCode;
        _callback = OnRecord;
    }

    internal static IDisposable? Start(Action<PixelPoint?, bool> press)
    {
        if (!OperatingSystem.IsLinux()) return null;
        var listener = new X11OptionsDismiss(press, 0);
        try
        {
            listener._control = XOpenDisplay(IntPtr.Zero);
            if (listener._control == IntPtr.Zero || !XRecordQueryVersion(listener._control, out _, out _)) return Fail();
            listener._data = XOpenDisplay(IntPtr.Zero);
            if (listener._data == IntPtr.Zero) return Fail();
            listener._escapeCode = XKeysymToKeycode(listener._control, 0xff1b);
            var range = XRecordAllocRange();
            if (range == IntPtr.Zero) return Fail();
            try
            {
                Marshal.StructureToPtr(new RecordRange { Device = new Range8 { First = 2, Last = 4 } }, range, false);
                nuint allClients = 3; // XRecordAllClients; device events are recorded once, before delivery.
                listener._context = XRecordCreateContext(listener._control, 0, ref allClients, 1, ref range, 1);
            }
            finally { XFree(range); }
            if (listener._context == 0) return Fail();
            XSync(listener._control, false); // context must exist before enabling on the data connection
            if (!XRecordEnableContextAsync(listener._data, listener._context, listener._callback, IntPtr.Zero)) return Fail();
            new Thread(listener.Listen) { IsBackground = true, Name = "emi-options-xrecord" }.Start();
            return listener;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            Log.Debug(ex, "[EmiDesk] XRecord dismissal unavailable");
            return Fail();
        }
        IDisposable? Fail() { listener.Cleanup(); return null; }
    }

    private void Listen()
    {
        try
        {
            var fd = new PollFd { Fd = XConnectionNumber(_data), Events = 1 };
            while (Volatile.Read(ref _disposed) == 0)
            {
                XRecordProcessReplies(_data);
                if (Volatile.Read(ref _disposed) != 0) break;
                if (poll(ref fd, 1, -1) > 0 && (fd.Returned & (8 | 16 | 32)) != 0) break;
            }
        }
        finally { lock (_gate) Cleanup(); }
    }

    private void OnRecord(IntPtr closure, IntPtr data)
    {
        try
        {
            var record = Marshal.PtrToStructure<RecordData>(data);
            if (record.Category != 0 || Volatile.Read(ref _disposed) != 0) return;
            // Only core key/button events were requested. Decode complete 32-byte wire events.
            var bytes = new byte[32];
            for (nuint offset = 0; offset + 8 <= record.Length; offset += 8)
            {
                Marshal.Copy(record.Data + checked((int)(offset * 4)), bytes, 0, 32);
                Route(bytes, record.Swapped != 0);
            }
        }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] XRecord dismissal callback failed"); }
        finally { XRecordFreeData(data); }
    }

    internal void Route(ReadOnlySpan<byte> wire, bool swapped = false)
    {
        if (Volatile.Read(ref _disposed) != 0 || wire.Length < 32) return;
        int type = wire[0] & 0x7f, detail = wire[1];
        if (type == 2 && detail != 0 && detail == _escapeCode) _press(null, true);
        else if (type == 4 && detail is 1 or 3)
        {
            bool little = BitConverter.IsLittleEndian != swapped;
            int x = little ? BinaryPrimitives.ReadInt16LittleEndian(wire[20..]) : BinaryPrimitives.ReadInt16BigEndian(wire[20..]);
            int y = little ? BinaryPrimitives.ReadInt16LittleEndian(wire[22..]) : BinaryPrimitives.ReadInt16BigEndian(wire[22..]);
            _press(new PixelPoint(x, y), false);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        lock (_gate)
        {
            if (_context == 0) return; // test listener, or worker already cleaned up
            XRecordDisableContext(_control, _context);
            XFlush(_control); // wakes ProcessReplies; the worker owns connection cleanup
        }
    }

    private void Cleanup()
    {
        if (_context != 0) { XRecordFreeContext(_control, _context); _context = 0; }
        if (_data != IntPtr.Zero) { XCloseDisplay(_data); _data = IntPtr.Zero; }
        if (_control != IntPtr.Zero) { XCloseDisplay(_control); _control = IntPtr.Zero; }
    }
}
