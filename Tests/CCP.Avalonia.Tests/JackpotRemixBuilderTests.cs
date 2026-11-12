using System;
using System.IO;
using System.Linq;
using System.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;

namespace CCP.Avalonia.Tests;

public class JackpotRemixBuilderTests
{
    [Fact]
    public void SourcesFilterBeforeCyclingAndRespectLibraryAndSize()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var outside = root + ".gif";
        try
        {
            var small = Path.Combine(root, "small.gif");
            var large = Path.Combine(root, "large.gif");
            File.WriteAllBytes(small, new byte[3]);
            File.WriteAllBytes(large, new byte[5]);
            File.WriteAllBytes(outside, new byte[3]);
            var result = JackpotRemixBuilder.Sources(root, new[] { outside, large, small, Path.Combine(root, "missing") }, 4);
            Assert.Equal(8, result.Count);
            Assert.All(result, path => Assert.Equal(small, path));
        }
        finally { Directory.Delete(root, true); File.Delete(outside); }
    }

    [Fact]
    public void OutputRejectsInvalidAndOversizeDataWithoutCreatingCache()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Assert.Null(JackpotRemixBuilder.WriteOutput(root, "x", "not base64", CancellationToken.None));
        Assert.Null(JackpotRemixBuilder.WriteOutput(root, "x", Convert.ToBase64String(new byte[10]), CancellationToken.None));
        Assert.Null(JackpotRemixBuilder.WriteOutput(root, "x", Convert.ToBase64String("GIF89aextra"u8.ToArray()), CancellationToken.None, 6));
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public void OutputWritesGifAndBoundsCache()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            foreach (var name in new[] { "remix-old1.gif", "remix-old2.gif" })
            {
                var file = Path.Combine(root, name);
                File.WriteAllText(file, "old");
                File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddHours(-1));
            }
            var bytes = "GIF89atest"u8.ToArray();
            var path = JackpotRemixBuilder.WriteOutput(root, "sample", Convert.ToBase64String(bytes), CancellationToken.None);
            Assert.NotNull(path);
            Assert.Equal(bytes, File.ReadAllBytes(path));
            Assert.Equal(2, Directory.GetFiles(root, "remix-*.gif").Length);
            Assert.Empty(Directory.GetFiles(root, "*.tmp"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void CancelledOutputDoesNotCreateCache()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Assert.Null(JackpotRemixBuilder.WriteOutput(root, "x", Convert.ToBase64String("GIF89a"u8.ToArray()), new CancellationToken(true)));
        Assert.False(Directory.Exists(root));
    }
}
