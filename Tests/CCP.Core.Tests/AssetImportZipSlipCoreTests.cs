using System;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>
/// WPF main c16437a5fe / 627d6dd846 / 84a1dd8005 (Zip Slip) and a40b1daa81 (#1515 skip identical) on the
/// Core <see cref="AssetImportService"/>: a ZIP entry never writes outside the asset folder, and a
/// second import of the same pack adds nothing.
/// </summary>
[Collection(SessionStatics.Name)]
public sealed class AssetImportZipSlipCoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ccp-zipslip-" + Guid.NewGuid().ToString("N"));
    private readonly string _assets;
    private readonly string? _oldCustom = CoreSettings.Current.CustomAssetsPath;
    private readonly Func<string>? _oldProvider = CorePaths.EffectiveAssetsProvider;

    public AssetImportZipSlipCoreTests()
    {
        _assets = Path.Combine(_root, "assets");
        Directory.CreateDirectory(_assets);
        CoreSettings.Current.CustomAssetsPath = _assets;
        CorePaths.EffectiveAssetsProvider = () => _assets;
    }

    public void Dispose()
    {
        CoreSettings.Current.CustomAssetsPath = _oldCustom;
        CorePaths.EffectiveAssetsProvider = _oldProvider;
        try { Directory.Delete(_root, true); } catch { }
    }

    private string Zip(params string[] entries)
    {
        var path = Path.Combine(_root, "pack.zip");
        File.Delete(path);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var name in entries)
        {
            using var s = zip.CreateEntry(name).Open();
            s.Write(new byte[] { 1, 2, 3 });
        }
        return path;
    }

    [Fact]
    public async Task A_zip_entry_climbing_out_of_the_asset_folder_is_skipped()
    {
        var zip = Zip("pack/ok.png", "pack/../../../evil.png", "pack/../../escape/evil2.png");

        var result = await new AssetImportService().ImportAsync(new[] { zip });

        Assert.Equal(1, result.ImagesImported);
        Assert.Equal(2, result.Skipped);
        Assert.True(File.Exists(Path.Combine(_assets, "images", "pack", "ok.png")));
        Assert.False(File.Exists(Path.Combine(_root, "evil.png")));
        Assert.False(File.Exists(Path.Combine(_assets, "evil.png")));
        Assert.False(Directory.Exists(Path.Combine(_assets, "escape")));
    }

    [Fact]
    public async Task Importing_the_same_pack_twice_adds_nothing()
    {
        var zip = Zip("pack/a.png", "pack/b.mp4");
        var first = await new AssetImportService().ImportAsync(new[] { zip });
        var second = await new AssetImportService().ImportAsync(new[] { zip });

        Assert.Equal(2, first.TotalImported);
        Assert.Equal(0, second.TotalImported);
        Assert.Equal(2, second.Skipped);
        Assert.False(File.Exists(Path.Combine(_assets, "images", "pack", "a_1.png")));
    }

    [Theory]
    [InlineData("pack", true)]
    [InlineData("pack/sub", true)]
    [InlineData("../outside", false)]
    [InlineData("pack/../../outside", false)]
    public void IsInsideFolder_matches_the_inline_guard(string relative, bool inside) =>
        Assert.Equal(inside, AssetImportService.IsInsideFolder(_assets, Path.Combine(_assets, relative)));
}
