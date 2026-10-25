using System;
using System.IO;
using ConditioningControlPanel;
using ConditioningControlPanel.Models.Deeper;
using ConditioningControlPanel.Services.Deeper;
using Xunit;

namespace CCP.Core.Tests;

[Collection(SessionStatics.Name)]
public sealed class DeeperShellEntryTests
{
    [Fact]
    public void ImportPreservesBatchOutcomesAndDoesNotDuplicateAReformattedFile()
    {
        using var source = new TempFolder();
        using var library = new EnhancementLibrary();
        var path = Path.Combine(source.Path, "source.json");
        var enhancement = new Enhancement { MediaType = MediaTypes.Audio, MediaSource = "sample.mp3" };
        enhancement.Metadata.Name = "import-test-" + Guid.NewGuid().ToString("N");
        File.WriteAllText(path, EnhancementSerializer.Save(enhancement));
        var settings = Path.Combine(source.Path, "settings.json");
        File.WriteAllText(settings, "{}");
        string? saved = null;
        try
        {
            var result = EnhancementImportBatch.Run(library, new[] { path, settings, "wrong.txt" });
            saved = result.LastImportedPath;
            Assert.Single(result.Imported);
            Assert.Single(result.Errors);
            Assert.Equal(1, result.Skipped);
            Assert.True(File.Exists(saved));
            Assert.Equal(source.Path, CoreSettings.Current.DeeperLastDirectory);
            File.WriteAllText(path, Newtonsoft.Json.Linq.JToken.Parse(File.ReadAllText(path)).ToString(Newtonsoft.Json.Formatting.None));
            var duplicate = EnhancementImportBatch.Run(library, new[] { path });
            Assert.Empty(duplicate.Imported);
            Assert.Equal(saved, duplicate.DuplicatePath);
            Assert.Equal(saved, EnhancementImportBatch.Run(library, new[] { saved! }).DuplicatePath);
        }
        finally { if (saved != null) File.Delete(saved); }
    }

    [Fact]
    public void FileOpenUsesFirstFlagAndRejectsMissingUnsupportedAndNetworkPaths()
    {
        using var dir = new TempFolder();
        var media = Path.Combine(dir.Path, "sample.MP4");
        File.WriteAllText(media, "test");
        Assert.Equal(("edit", media), DeeperFileOpen.Parse(new[] { "--edit", media, "--play", media }));
        Assert.Equal(("play", media), DeeperFileOpen.Parse(new[] { "--play", media }));
        Assert.Equal(("edit", media), DeeperFileOpen.Decode(DeeperFileOpen.Encode(new[] { "--edit", media })));
        Assert.Null(DeeperFileOpen.Decode("deeper:destroy\n" + media).Action);
        Assert.Null(DeeperFileOpen.Decode("game:race").Action);
        Assert.Null(DeeperFileOpen.Parse(new[] { "--PLAY", media }).Action);
        Assert.Null(DeeperFileOpen.Parse(new[] { "--edit" }).Action);
        Assert.Null(DeeperFileOpen.Parse(new[] { "--play", media + ".missing" }).Action);
        Assert.Null(DeeperFileOpen.Parse(new[] { "--play", @"\\server\sample.mp4" }).Action);
        var text = Path.Combine(dir.Path, "sample.txt");
        File.WriteAllText(text, "test");
        Assert.Null(DeeperFileOpen.Parse(new[] { "--edit", text }).Action);
    }

    private sealed class TempFolder : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("deeper-entry-").FullName;
        public void Dispose() => Directory.Delete(Path, true);
    }
}
