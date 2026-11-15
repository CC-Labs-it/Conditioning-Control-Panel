using System;
using System.IO;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class SherpaWakeSpotterTests
{
    private sealed class Handle : IDisposable
    {
        public bool Disposed;
        public void Dispose() => Disposed = true;
    }

    [Fact]
    public void MissingModelDoesNotInitialize()
    {
        int calls = 0;
        using var spotter = new SherpaWakeSpotter(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()), (_, _, _) => { calls++; return new Handle(); });
        Assert.False(spotter.IsAvailable(true, .15, 2));
        Assert.Equal(0, calls);
    }

    [Fact]
    public void FingerprintChangesRebuildAndFailureIsMemoizedUntilReset()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(root);
        try
        {
            foreach (var name in new[] { "encoder.onnx", "decoder.onnx", "joiner.onnx", "tokens.txt", "keywords.txt" })
                File.WriteAllText(Path.Combine(root, name), "");
            int calls = 0;
            var first = new Handle();
            using var spotter = new SherpaWakeSpotter(root, (_, _, _) =>
            {
                calls++;
                if (calls > 1) throw new InvalidOperationException();
                return first;
            });
            Assert.False(spotter.IsAvailable(false, .15, 2));
            Assert.True(spotter.IsAvailable(true, .15, 2));
            Assert.True(spotter.IsAvailable(true, .15, 2));
            Assert.Equal(1, calls);
            Assert.False(spotter.IsAvailable(true, .20, 2));
            Assert.True(first.Disposed);
            Assert.False(spotter.IsAvailable(true, .20, 2));
            Assert.Equal(2, calls);
            spotter.ResetInitState();
            Assert.False(spotter.IsAvailable(true, .20, 2));
            Assert.Equal(3, calls);
        }
        finally { Directory.Delete(root, true); }
    }
}
