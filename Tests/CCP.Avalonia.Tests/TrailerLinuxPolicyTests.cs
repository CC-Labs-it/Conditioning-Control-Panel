using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;
namespace CCP.Avalonia.Tests;
public sealed class TrailerLinuxPolicyTests {
    private sealed class Signals : TrailerLinuxPolicy.ISignals {
        public int References, Releases;
        public int FailAt;
        public readonly Dictionary<string, Delegate> Callbacks = new();
        public readonly List<ulong> Disconnected = new();
        public void Ref(IntPtr view) => References++;
        public void Unref(IntPtr view) => Releases++;
        public ulong Connect(IntPtr view, string signal, Delegate callback) {
            Callbacks.Add(signal, callback);
            return Callbacks.Count == FailAt ? 0 : (ulong)Callbacks.Count;
        }
        public void Disconnect(IntPtr view, ulong id) => Disconnected.Add(id);
    }
    // WPF Controls/ChasterTrailerView.cs:152,156,164,197-209.
    [Fact]
    public void WebKitSuppressesContextMenuAndErrorDocumentAndUnsubscribesOnce() {
        var native = new Signals(); var queued = new List<Action>(); var failures = 0;
        var policy = new TrailerLinuxPolicy((IntPtr)42, () => failures++, native, queued.Add);
        Assert.Equal(new[] { "web-process-terminated", "context-menu", "load-failed" }, native.Callbacks.Keys);
        Assert.Equal(1, native.Callbacks["context-menu"].DynamicInvoke(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero));
        Assert.Equal(1, native.Callbacks["load-failed"].DynamicInvoke(IntPtr.Zero, 0, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero));
        var terminated = native.Callbacks["web-process-terminated"];
        terminated.DynamicInvoke(IntPtr.Zero, 0, IntPtr.Zero);
        Assert.Equal(0, failures); Assert.Single(queued)(); Assert.Equal(1, failures);
        queued.Clear(); terminated.DynamicInvoke(IntPtr.Zero, 0, IntPtr.Zero);
        policy.Dispose(); policy.Dispose(); Assert.Single(queued)();
        Assert.Equal(1, failures); Assert.Equal(1, native.References); Assert.Equal(1, native.Releases);
        Assert.Equal(new ulong[] { 1, 2, 3 }, native.Disconnected);
    }
    [Theory, InlineData(1), InlineData(2), InlineData(3)]
    public void FailedSubscriptionReleasesEveryEarlierSignal(int failAt) {
        var native = new Signals { FailAt = failAt };
        Assert.Throws<InvalidOperationException>(() => new TrailerLinuxPolicy((IntPtr)42, () => { }, native, _ => { }));
        Assert.Equal(1, native.References); Assert.Equal(1, native.Releases);
        Assert.Equal(Enumerable.Range(1, failAt - 1).Select(i => (ulong)i), native.Disconnected);
    }
}
