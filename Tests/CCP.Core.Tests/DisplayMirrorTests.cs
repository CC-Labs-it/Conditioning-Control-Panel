using System;
using System.Collections.Generic;
using ConditioningControlPanel.Services;
using Xunit;
namespace CCP.Core.Tests;
public sealed class DisplayMirrorTests {
    private sealed class FakeTopology : IDisplayTopology {
        public int ActiveScreenCount { get; set; } = 2;
        public int NextResult { get; set; }
        public bool Throw { get; set; }
        public List<bool> Calls { get; } = new();
        public int Apply(bool clone) {
            Calls.Add(clone);
            if (Throw) throw new InvalidOperationException("driver");
            return NextResult;
        }
    }
    [Fact]
    public void SingleActiveScreenLeavesTheTopologyUntouched() {
        var topo = new FakeTopology { ActiveScreenCount = 1 };
        var mirror = new DisplayMirror(topo);
        Assert.False(mirror.EnableMirror()); Assert.Empty(topo.Calls); Assert.False(mirror.IsMirroring);
    }
    [Fact]
    public void CloneOnceThenExtendOnceAndBothAreIdempotent() {
        var topo = new FakeTopology();
        var mirror = new DisplayMirror(topo);
        Assert.True(mirror.EnableMirror()); Assert.True(mirror.EnableMirror()); Assert.True(mirror.IsMirroring); Assert.True(mirror.DisableMirror()); Assert.True(mirror.DisableMirror());
        Assert.False(mirror.IsMirroring); Assert.Equal(new[] { true, false }, topo.Calls);
    }
    [Fact]
    public void RefusedCloneIsNotMirroringAndNothingIsRestored() {
        var topo = new FakeTopology { NextResult = 50 };
        var mirror = new DisplayMirror(topo);
        Assert.False(mirror.EnableMirror()); Assert.False(mirror.IsMirroring);
        mirror.Dispose();
        Assert.Equal(new[] { true }, topo.Calls);
    }
    [Fact]
    public void FailedRestoreStaysMirroringSoDisposeRetries() {
        var topo = new FakeTopology();
        var mirror = new DisplayMirror(topo);
        mirror.EnableMirror();
        topo.NextResult = 31;
        Assert.False(mirror.DisableMirror()); Assert.True(mirror.IsMirroring);
        topo.NextResult = 0;
        mirror.Dispose();
        mirror.Dispose();
        Assert.False(mirror.IsMirroring); Assert.Equal(new[] { true, false, false }, topo.Calls);
    }
    [Fact]
    public void ThrowingDriverReportsFailureInsteadOfEscaping() {
        var topo = new FakeTopology { Throw = true };
        var mirror = new DisplayMirror(topo);
        Assert.False(mirror.EnableMirror()); Assert.False(mirror.IsMirroring);
    }
}
