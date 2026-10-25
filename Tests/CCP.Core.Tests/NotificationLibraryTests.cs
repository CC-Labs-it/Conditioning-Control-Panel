using System;
using System.IO;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

public sealed class NotificationLibraryTests
{
    [Theory]
    [InlineData(".PNG", true, true)]
    [InlineData(".jpeg", true, true)]
    [InlineData(".gif", true, false)]
    [InlineData(".webp", false, true)]
    [InlineData(".mp4", false, false)]
    public void ProbeRecursesAndUsesWpfExtensions(string extension, bool wallpaper, bool expected)
    {
        var root = Path.Combine(Path.GetTempPath(), "ccp-notification-" + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.False(NotificationLibrary.HasMedia(root, wallpaper));
            Directory.CreateDirectory(Path.Combine(root, "nested"));
            Assert.False(NotificationLibrary.HasMedia(root, wallpaper));
            File.WriteAllText(Path.Combine(root, "nested", "fixture" + extension), "fixture");
            Assert.Equal(expected, NotificationLibrary.HasMedia(root, wallpaper));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
