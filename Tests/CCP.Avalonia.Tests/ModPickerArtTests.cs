using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ModPickerArtTests
{
    [Fact]
    public void CatalogCardsUseShippedArtEvenWhenTheActiveModOverridesEveryImage() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .WithInterFont()
                .With(ConditioningControlPanel.Avalonia.Platform.AppFonts.Options())
                .ConfigureFonts(ConditioningControlPanel.Avalonia.Platform.AppFonts.Register)
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();

        var dir = Directory.CreateTempSubdirectory("ccp-picker-art-");
        var overridePath = Path.Combine(dir.FullName, "override.png");
        using (var bitmap = new WriteableBitmap(new PixelSize(6, 3), new Vector(96, 96)))
            bitmap.Save(overridePath);
        var previous = CoreModArt.OverridePathProvider;
        CoreModArt.OverridePathProvider = _ => overridePath;
        ModPickerDialog? dialog = null;
        try
        {
            dialog = new ModPickerDialog(null);
            var cards = dialog.FindControl<ItemsControl>("CardsList")!.ItemsSource!.Cast<ModPickerCard>().ToList();
            Assert.Equal(6, cards.Count);
            foreach (var entry in ModPickerCatalog.All)
            {
                // WPF binds ModPackCatalog.ArtUri directly to compiled art, bypassing the resolver.
                using var stream = AssetLoader.Open(new Uri("avares://CCP.Avalonia/Resources/" + entry.ArtName));
                using var shipped = new Bitmap(stream);
                var actual = Assert.IsType<Bitmap>(cards.Single(c => c.ModId == entry.ModId).Art);
                Assert.Equal(shipped.PixelSize, actual.PixelSize);
                Assert.Equal(Digest(shipped), Digest(actual));
            }
        }
        finally
        {
            dialog?.Close();
            CoreModArt.OverridePathProvider = previous;
            dir.Delete(true);
        }
    });

    private static byte[] Digest(Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream);
        return SHA256.HashData(stream.ToArray());
    }
}
