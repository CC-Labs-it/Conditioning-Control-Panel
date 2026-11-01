using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class VideoThumbnailTests
{
    [Fact]
    public async Task DecoderPassesThePathAsOneArgumentAndRejectsErrors()
    {
        if (!OperatingSystem.IsLinux()) return;
        var root = Directory.CreateTempSubdirectory("ccp-thumb-").FullName;
        try
        {
            var decoder = Path.Combine(root, "decoder");
            File.WriteAllText(decoder, "#!/bin/sh\nprintf '%s' \"$5\"\n");
            File.SetUnixFileMode(decoder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var path = Path.Combine(root, "pack clip; name.mp4");
            Assert.Equal(path, Encoding.UTF8.GetString((await VideoThumbnail.ReadAsync(path, decoder))!));
            File.WriteAllText(decoder, "#!/bin/sh\nprintf bad\nexit 1\n");
            Assert.Null(await VideoThumbnail.ReadAsync(path, decoder));
            Assert.Null(await VideoThumbnail.ReadAsync(path, Path.Combine(root, "missing")));
        }
        finally { Directory.Delete(root, true); }
    }
}
