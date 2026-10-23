using System;
using System.IO;
using System.Threading.Tasks;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

[Collection(SessionStatics.Name)]
public sealed class VideoMetadataCacheTests
{
    [Fact]
    public async Task Browser_only_cache_misses_are_unknown_and_metadata_is_reused_until_file_changes()
    {
        var dir = Directory.CreateTempSubdirectory("ccp-duration-").FullName;
        var path = Path.Combine(dir, "clip.mp4");
        try
        {
            File.WriteAllBytes(path, new byte[] { 1, 2 });
            var cache = new VideoMetadataCache(null);
            Assert.Null(await cache.GetOrComputeDurationAsync(path));
            cache.StoreDuration(path, 12.5);
            Assert.Equal(12.5, cache.TryGetDuration(path));
            Assert.Equal(12.5, await cache.GetOrComputeDurationAsync(path));
            cache.StoreDuration(path, double.NaN);
            cache.StoreDuration(path, double.PositiveInfinity);
            cache.StoreDuration(path, 0);
            Assert.Equal(12.5, cache.TryGetDuration(path));
            File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
            Assert.Null(cache.TryGetDuration(path));
            Assert.Null(await cache.GetOrComputeDurationAsync(path));
        }
        finally { Directory.Delete(dir, true); }
    }
}
