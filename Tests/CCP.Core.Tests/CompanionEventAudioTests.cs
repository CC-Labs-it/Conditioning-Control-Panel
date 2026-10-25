using System;
using System.IO;
using ConditioningControlPanel.Services.Companion;
using Xunit;

namespace CCP.Core.Tests;

public sealed class CompanionEventAudioTests
{
    [Fact]
    public void ExactModClipUsesGeneratorSlugOtherwiseStaysSilent()
    {
        var folder = Path.Combine(Path.GetTempPath(), "ccp-event-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var clip = Path.Combine(folder, "giggles time for a surprise.mp3");
            File.WriteAllBytes(clip, new byte[] { 0 });
            Assert.Equal(clip, CompanionEventAudio.ResolveInFolder("*giggles* Time for a {petname} surprise~", folder));
            Assert.Null(CompanionEventAudio.ResolveInFolder("another phrase", folder));
            Assert.Null(CompanionEventAudio.ResolveInFolder("{petname}~", folder));
            Assert.Null(CompanionEventAudio.ResolveInFolder(null, folder));
            Assert.Null(CompanionEventAudio.ResolveInFolder("*giggles* Time for a surprise~", null));
        }
        finally { Directory.Delete(folder, true); }
    }
}
