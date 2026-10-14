using System.IO;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

// Callers spell relative paths the Windows way; off Windows '\' must fold to '/' or nothing resolves.
// Rooted paths never reach Normalize: Resolve/ResolveDirectory hand them back untouched.
public sealed class ContentLocatorNormalizeTests
{
    [Fact]
    public void Backslashes_fold_to_the_platform_separator()
    {
        var expected = Path.Combine("Resources", "Audio", "backroom", "words");
        Assert.Equal(expected, ContentLocator.Normalize(@"Resources\Audio\backroom\words"));
        Assert.Equal(expected, ContentLocator.Normalize("Resources/Audio/backroom/words"));
        Assert.Equal(expected, ContentLocator.Normalize(@" \Resources\Audio/backroom\words"));
    }

    [Fact]
    public void Web_content_mirror_is_anchored_below_the_pack_root()
        => Assert.Equal(Path.Combine(ContentLocator.ContentRoot, "Resources", "web"), ContentLocator.ContentWebRoot);

    [Theory]
    [InlineData("intake/assets/vo/q.mp3", true)]
    [InlineData("intake/assets/music/bed.OGG", true)]
    [InlineData("intake/assets/sfx/click.wav", true)]
    [InlineData("dtrh/assets/bubbles/pop.mp3", true)]
    [InlineData("intake/assets/vo/manifest.json", false)]
    [InlineData("intake/assets/vo/../../../profile.mp3", false)]
    [InlineData("intake/assets/vo/.hidden/a.mp3", false)]
    [InlineData("intake/assets/vo/boot.js", false)]
    [InlineData("companion_audio/private.mp3", false)]
    public void Intake_web_pack_fallback_is_audio_only(string path, bool expected)
        => Assert.Equal(expected, ContentLocator.IsIntakeWebAudio(path));

    [Fact]
    public void Rooted_paths_pass_through_untouched()
    {
        var abs = Path.Combine(Path.GetTempPath(), "x", "clip.mp3");
        Assert.Equal(abs, ContentLocator.Resolve(abs));
        Assert.Equal(abs, ContentLocator.ResolveDirectory(abs));
        if (!System.OperatingSystem.IsWindows()) return;
        foreach (var p in new[] { @"\\server\share\clip.mp3", @"\\?\C:\x\clip.mp3", @"C:\x\clip.mp3" })
        {
            Assert.Equal(p, ContentLocator.Resolve(p));
            Assert.Equal(p, ContentLocator.ResolveDirectory(p));
        }
    }
}
