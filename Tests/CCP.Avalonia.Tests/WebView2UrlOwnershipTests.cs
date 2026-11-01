using System;
using System.Runtime.InteropServices;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class WebView2UrlOwnershipTests
{
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int ReleaseReference(IntPtr instance);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DocumentReadReleasesTheOwnedComReferenceEvenWhenReadThrows(bool fail)
    {
        var vtable = Marshal.AllocHGlobal(3 * IntPtr.Size);
        var core = Marshal.AllocHGlobal(IntPtr.Size);
        int releases = 0;
        IntPtr released = IntPtr.Zero;
        ReleaseReference release = instance => { releases++; released = instance; return 0; };
        try
        {
            Marshal.WriteIntPtr(vtable, 2 * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(release));
            Marshal.WriteIntPtr(core, vtable);
            string? Read(IntPtr pointer)
            {
                Assert.Equal(core, pointer);
                if (fail) throw new InvalidOperationException("read failed");
                return "https://example.com/";
            }

            if (fail) Assert.Throws<InvalidOperationException>(() => ScrolllerWebHostGate.ReadAndReleaseWebView2Url(core, Read));
            else Assert.Equal("https://example.com/", ScrolllerWebHostGate.ReadAndReleaseWebView2Url(core, Read));
            Assert.Equal(1, releases);
            Assert.Equal(core, released);
        }
        finally
        {
            GC.KeepAlive(release);
            Marshal.FreeHGlobal(core);
            Marshal.FreeHGlobal(vtable);
        }
    }

    [Fact]
    public void MissingCoreDoesNotAttemptARead() =>
        Assert.Null(ScrolllerWebHostGate.ReadAndReleaseWebView2Url(IntPtr.Zero, _ => throw new InvalidOperationException()));
}
