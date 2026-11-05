using System;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Models.Deeper;
using ConditioningControlPanel.Services.Deeper;
using Xunit;

namespace CCP.Core.Tests;

public sealed class DeeperHubDurationTests
{
    [Fact]
    public void LocalIndexUsesCachedDurationButAuthoredDurationWins()
    {
        var folder = Directory.CreateTempSubdirectory("ccp-hub-duration-");
        try
        {
            var media = Path.Combine(folder.FullName, "clip.mp3");
            File.WriteAllText(media, "synthetic media; no decoder needed");
            var enhancement = new Enhancement { MediaType = MediaTypes.Audio, MediaSource = media };
            var path = Path.Combine(folder.FullName, "project.ccpenh.json");
            File.WriteAllText(path, EnhancementSerializer.Save(enhancement));
            MediaDurationCache.Remember(media, 125);
            Assert.Equal(125, DeeperLocalLibrary.Scan(folder.FullName).Entries.Single().DurationSeconds);
            enhancement.Metadata.MediaDurationSeconds = 30;
            File.WriteAllText(path, EnhancementSerializer.Save(enhancement));
            Assert.Equal(30, DeeperLocalLibrary.Scan(folder.FullName).Entries.Single().DurationSeconds);
        }
        finally { folder.Delete(true); }
    }
}
