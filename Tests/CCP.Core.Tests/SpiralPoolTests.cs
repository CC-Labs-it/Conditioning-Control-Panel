using System;
using System.IO;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

public sealed class SpiralPoolTests
{
    [Fact]
    public void Refuses_bare_personal_and_drive_roots_but_allows_dedicated_subfolders()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.True(PersonalFolderRoots.IsPersonalFolderRoot(null));
        Assert.True(PersonalFolderRoots.IsPersonalFolderRoot("\0"));
        Assert.True(PersonalFolderRoots.IsPersonalFolderRoot(Path.GetPathRoot(Path.GetTempPath())));
        if (!string.IsNullOrEmpty(profile))
        {
            Assert.True(PersonalFolderRoots.IsPersonalFolderRoot(profile));
            Assert.True(PersonalFolderRoots.IsPersonalFolderRoot(Path.Combine(profile, "Pictures")));
            Assert.True(PersonalFolderRoots.IsPersonalFolderRoot(Path.Combine(profile, "Downloads") + Path.DirectorySeparatorChar));
            Assert.False(PersonalFolderRoots.IsPersonalFolderRoot(Path.Combine(profile, "Pictures", "spirals")));
        }
    }

    [Fact]
    public void Random_pool_uses_configured_folder_filters_media_and_never_repeats()
    {
        var dir = Directory.CreateTempSubdirectory("ccp-spiral-pool-");
        try
        {
            var first = Path.Combine(dir.FullName, "first.GIF");
            var second = Path.Combine(dir.FullName, "second.webp");
            File.WriteAllText(first, "test");
            File.WriteAllText(second, "test");
            File.WriteAllText(Path.Combine(dir.FullName, "video.mp4"), "test");
            File.WriteAllText(Path.Combine(dir.FullName, "notes.txt"), "test");
            var pool = new SpiralPool(new Random(641));
            string? previous = null;
            for (var i = 0; i < 20; i++)
            {
                var pick = pool.Pick(first, Path.Combine(dir.FullName, "missing"));
                Assert.Contains(pick, new[] { first, second });
                Assert.NotEqual(previous, pick);
                previous = pick;
            }
            File.Delete(second);
            Assert.Equal(first, pool.Pick(first, dir.FullName));
            File.Delete(first);
            Assert.Null(pool.Pick(null, dir.FullName));
        }
        finally { dir.Delete(true); }
    }

    [Fact]
    public void A_personal_folder_selection_falls_back_to_the_explicit_library()
    {
        var dir = Directory.CreateTempSubdirectory("ccp-spiral-pool-");
        try
        {
            var image = Path.Combine(dir.FullName, "library.png");
            File.WriteAllText(image, "test");
            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            // No personal folder is scanned or written: the synthetic selected path only chooses the pool.
            var selected = Path.Combine(profile, "Pictures", "selected.gif");
            Assert.Equal(image, new SpiralPool().Pick(selected, dir.FullName));
            Assert.Equal(image, new SpiralPool().Pick(null, dir.FullName));
        }
        finally { dir.Delete(true); }
    }
}
