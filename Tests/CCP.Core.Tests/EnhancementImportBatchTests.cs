using System;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Models.Deeper;
using ConditioningControlPanel.Services.Deeper;
using Xunit;

namespace CCP.Core.Tests;

public sealed class EnhancementImportBatchTests
{
    [Fact]
    public void ImportPromotesAndReimportRevealsExistingWithoutAnotherCopy()
    {
        var source = Directory.CreateTempSubdirectory("ccp-enh-import-");
        using var library = new EnhancementLibrary();
        var name = "batch-" + Guid.NewGuid().ToString("N");
        string? saved = null;
        try
        {
            var input = Path.Combine(source.FullName, "input.json");
            var enhancement = new Enhancement { MediaType = MediaTypes.Audio, MediaSource = "test.mp3" };
            enhancement.Metadata.Name = name;
            File.WriteAllText(input, EnhancementSerializer.Save(enhancement));
            var first = EnhancementImportBatch.Run(library, new[] { input });
            Assert.Empty(first.Errors);
            Assert.Single(first.Imported);
            saved = first.RevealPath;
            Assert.NotNull(saved);
            Assert.True(File.Exists(saved));
            Assert.Equal(name, EnhancementSerializer.LoadFromFile(saved).Metadata.Name);
            var again = EnhancementImportBatch.Run(library, new[] { input });
            Assert.Empty(again.Imported);
            Assert.Equal(saved, again.DuplicateOfPath);
            Assert.Equal(saved, again.RevealPath);
            Assert.Single(library.ScanLibrary().Where(e => e.Name == name));
            Assert.Equal(saved, EnhancementImportBatch.Run(library, new[] { saved }).RevealPath);
        }
        finally { if (saved != null) File.Delete(saved); source.Delete(true); }
    }

    [Fact]
    public void BatchSkipsOtherJsonReportsErrorsAndContinuesToValidFile()
    {
        var source = Directory.CreateTempSubdirectory("ccp-enh-batch-");
        using var library = new EnhancementLibrary();
        string? saved = null;
        try
        {
            var settings = Path.Combine(source.FullName, "settings.json");
            File.WriteAllText(settings, "{}");
            var bad = Path.Combine(source.FullName, "bad.ccpenh.json");
            File.WriteAllText(bad, "{\"$schema\":\"" + Enhancement.SchemaTag + "\", invalid}");
            var good = Path.Combine(source.FullName, "good.ccpenh.json");
            var enhancement = new Enhancement();
            enhancement.Metadata.Name = Guid.NewGuid().ToString("N");
            File.WriteAllText(good, EnhancementSerializer.Save(enhancement));
            var batch = EnhancementImportBatch.Run(library, new[] { settings, bad, "no.txt", good });
            saved = batch.LastImportedPath;
            Assert.Equal(1, batch.SkippedNonEnhancement);
            Assert.Equal(2, batch.Errors.Count);
            Assert.Contains(batch.Errors, e => e.StartsWith("bad.ccpenh.json:"));
            Assert.Single(batch.Imported);
            Assert.NotNull(saved);
            Assert.Equal(saved, batch.RevealPath);
        }
        finally { if (saved != null) File.Delete(saved); source.Delete(true); }
    }
}
