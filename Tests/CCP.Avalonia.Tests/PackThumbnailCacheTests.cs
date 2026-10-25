using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class PackThumbnailCacheTests
{
    [Fact]
    public Task UsesLruEntryAndByteLimitsAndRetriesFailures() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var settings = new ConditioningControlPanel.Models.AppSettings();
        var first = new ConditioningControlPanel.Services.ContentPackStore(() => "", () => settings, () => { });
        var second = new ConditioningControlPanel.Services.ContentPackStore(() => "", () => settings, () => { });
        Assert.Same(PackThumbnailCache.For(first), PackThumbnailCache.For(first));
        Assert.NotSame(PackThumbnailCache.For(first), PackThumbnailCache.For(second));
        var cache = new PackThumbnailCache();
        using var small = new global::Avalonia.Media.Imaging.WriteableBitmap(new PixelSize(8, 8), new Vector(96, 96));
        global::Avalonia.Media.Imaging.Bitmap? Unexpected() => throw new InvalidOperationException("cache miss");
        for (var i = 0; i < 50; i++) cache.GetOrLoad(i.ToString(), () => small);
        Assert.Same(small, cache.GetOrLoad("0", Unexpected)); // promote oldest
        cache.GetOrLoad("50", () => small);
        Assert.Same(small, cache.GetOrLoad("0", Unexpected));
        var loads = 0;
        cache.GetOrLoad("1", () => { loads++; return null; }); // evicted, failure not retained
        cache.GetOrLoad("1", () => { loads++; return small; });
        Assert.Equal(2, loads);
        Assert.Same(small, cache.GetOrLoad("1", Unexpected));

        var bounded = new PackThumbnailCache();
        using var large = new global::Avalonia.Media.Imaging.WriteableBitmap(new PixelSize(2048, 2048), new Vector(96, 96));
        // Each entry is 16 MiB; the fourth evicts the least recently used under 50 MiB.
        for (var i = 0; i < 3; i++) bounded.GetOrLoad(i.ToString(), () => large);
        bounded.GetOrLoad("0", Unexpected);
        bounded.GetOrLoad("3", () => large);
        Assert.Same(large, bounded.GetOrLoad("0", Unexpected));
        Assert.Same(large, bounded.GetOrLoad("2", Unexpected));
        Assert.Null(bounded.GetOrLoad("1", () => null));
        Assert.Equal(new PixelSize(2048, 2048), large.PixelSize); // eviction did not dispose visible images
        return Task.CompletedTask;
    });

}
