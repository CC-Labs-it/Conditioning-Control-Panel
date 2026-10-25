using System;
using System.IO;
using System.Threading.Tasks;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class FileDialogPlatformTests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData(42u, true)]
    [InlineData(43u, false)]
    public void PreviewCloseOnlyReactivatesOurForegroundProcess(uint? foreground, bool expected)
        => Assert.Equal(expected, PreviewFocus.MayReactivate(foreground, 42));

    [Theory]
    [InlineData("D:\\Media Library\\clip.mp4", "/select,\"D:\\Media Library\\clip.mp4\"")]
    [InlineData("", null)]
    [InlineData("bad\"path", null)]
    public void RevealQuotesOnlyThePath(string path, string? expected)
        => Assert.Equal(expected, ExternalOpener.RevealArguments(path));

    [Fact]
    public async Task MissingFileRevealsItsExistingParentAndMissingParentDoesNothing()
    {
        var root = Directory.CreateTempSubdirectory("ccp-reveal-");
        var previous = ExternalOpener.Shell;
        string? opened = null;
        ExternalOpener.Shell = path => { opened = path; return true; };
        try
        {
            Assert.True(await ExternalOpener.RevealAsync(null, Path.Combine(root.FullName, "gone.mp4")));
            Assert.Equal(root.FullName, opened);
            opened = null;
            Assert.False(await ExternalOpener.RevealAsync(null, Path.Combine(root.FullName, "absent", "gone.mp4")));
            Assert.Null(opened);
            Assert.False(await ExternalOpener.RevealAsync(null, null));
        }
        finally { ExternalOpener.Shell = previous; root.Delete(true); }
    }
}
