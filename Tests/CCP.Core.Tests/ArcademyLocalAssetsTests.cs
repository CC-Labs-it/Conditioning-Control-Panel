using System;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Services.Arcademy;
using Xunit;

namespace CCP.Core.Tests;

public class ArcademyLocalAssetsTests
{
    [Fact]
    public void ManifestIncludesOnlyActiveImagesAndMarksAnimatedWebp()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            var dir = Path.Combine(root, "images");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "a b.gif"), "");
            File.WriteAllText(Path.Combine(dir, "disabled.png"), "");
            File.WriteAllText(Path.Combine(dir, "other.txt"), "");
            var header = new byte[21];
            System.Text.Encoding.ASCII.GetBytes("RIFF").CopyTo(header, 0);
            System.Text.Encoding.ASCII.GetBytes("WEBPVP8X").CopyTo(header, 8);
            header[20] = 2;
            File.WriteAllBytes(Path.Combine(dir, "animated.webp"), header);
            var result = ArcademyLocalAssets.Build(root, new[] { "images/disabled.png" });
            Assert.Equal(new[] { "https://ccp.assets/images/a%20b.gif", "https://ccp.assets/images/animated.webp#.gif" }.OrderBy(x => x), result["gifs"]!.Values<string>().OrderBy(x => x));
            Assert.Empty(result["stills"]!);
        }
        finally { Directory.Delete(root, true); }
    }
}
