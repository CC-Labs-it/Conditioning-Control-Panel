using System;
using System.IO;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// ccp-bugs #1515: importing a pack twice made a name_1 copy of every file. A file whose name (or
/// one of its numbered copies) already holds the same byte size is skipped; a same-named file of
/// another size still gets the next free number.
/// </summary>
public sealed class AssetImportSkipIdenticalTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ccp-import-" + Guid.NewGuid().ToString("N"));

    public AssetImportSkipIdenticalTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private void Put(string name, int bytes) => File.WriteAllBytes(Path.Combine(_dir, name), new byte[bytes]);

    [Fact]
    public void A_free_name_is_used_as_is()
    {
        Assert.Equal(Path.Combine(_dir, "a.png"), AssetImportService.GetUniqueDestPath(_dir, "a.png", 10));
    }

    [Fact]
    public void The_same_name_and_size_is_skipped()
    {
        Put("a.png", 10);
        Assert.Null(AssetImportService.GetUniqueDestPath(_dir, "a.png", 10));
    }

    [Fact]
    public void The_same_name_with_another_size_still_gets_a_number()
    {
        Put("a.png", 10);
        Assert.Equal(Path.Combine(_dir, "a_1.png"), AssetImportService.GetUniqueDestPath(_dir, "a.png", 11));
    }

    [Fact]
    public void A_numbered_copy_from_an_earlier_import_counts_as_already_there()
    {
        Put("a.png", 10);
        Put("a_1.png", 11);
        Assert.Null(AssetImportService.GetUniqueDestPath(_dir, "a.png", 11));
        Assert.Equal(Path.Combine(_dir, "a_2.png"), AssetImportService.GetUniqueDestPath(_dir, "a.png", 12));
    }

    [Fact]
    public void A_skip_reads_as_already_existed_in_the_summary()
    {
        var result = new ImportResult { Skipped = 3 };
        Assert.Equal("No new files imported (3 already existed)", result.GetSummary());
    }

    [Theory]
    [InlineData("pack", true)]
    [InlineData("pack/sub", true)]
    [InlineData("../outside", false)]
    [InlineData("pack/../../outside", false)]
    public void A_zip_entry_never_writes_outside_the_asset_folder(string relative, bool inside)
    {
        Assert.Equal(inside, AssetImportService.IsInsideFolder(_dir, Path.Combine(_dir, relative)));
    }
}
