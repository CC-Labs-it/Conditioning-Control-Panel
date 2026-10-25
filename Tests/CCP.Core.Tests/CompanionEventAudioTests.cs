using System;
using System.IO;
using ConditioningControlPanel.Services.Companion;
using Xunit;

namespace CCP.Core.Tests;

public sealed class CompanionEventAudioTests
{
    [Fact]
    public void WpfSlugFindsOnlyAnExistingClipAndMissingAudioStaysSilent()
    {
        var folder = Path.Combine(Path.GetTempPath(), "ccp-events-" + Guid.NewGuid());
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "giggles pretty picture time.mp3");
            File.WriteAllText(path, "fixture");
            Assert.Equal(path, ModCompanionContent.ResolveEventAudioInFolder(folder,
                "*giggles* Pretty picture time, {petname}~"));
            Assert.Null(ModCompanionContent.ResolveEventAudioInFolder(folder, "A missing line"));
            Assert.Null(ModCompanionContent.ResolveEventAudioInFolder(null, "Pretty picture time"));
            Assert.Null(ModCompanionContent.ResolveEventAudioInFolder(folder, "{petname}~"));
            Assert.Null(ModCompanionContent.ResolveEventAudioInFolder(folder, null));
        }
        finally { Directory.Delete(folder, true); }
    }
}
