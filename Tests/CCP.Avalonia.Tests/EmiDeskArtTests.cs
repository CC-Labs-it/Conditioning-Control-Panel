using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>#3953: body and outfit overlay art. Moves shipped art files aside for a moment, so it runs alone.</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class EmiDeskArtTests
{
    // WPF EmiDeskWindow.xaml.cs:885-902, 938-1010: dress both layers, then restore standard.
    [Fact]
    public Task OutfitChangesRepaintBodyAndOverlayForTheCurrentPose() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var desk = new EmiDeskWindow();
        try
        {
            var body = desk.FindControl<Image>("BodyImage")!;
            var over = desk.FindControl<Image>("OutfitOverImage")!;
            desk.SetOutfit("labcoat");
            Assert.Equal("labcoat", desk.Outfit);
            AssertArt(EmiChains.BodyPath("idle", "labcoat")!, body);
            Assert.True(over.IsVisible);
            AssertArt(EmiChains.OverPath("labcoat", "idle")!, over);
            desk.SetPose("pet");
            AssertArt(EmiChains.BodyPath("pet", "labcoat")!, body);
            AssertArt(EmiChains.OverPath("labcoat", "pet")!, over);
            desk.SetOutfit("../unknown");
            Assert.Null(desk.Outfit);
            AssertArt(EmiChains.BodyPath("pet")!, body);
            Assert.False(over.IsVisible);
            Assert.Null(over.Source);
        }
        finally { desk.ShutDown(); }
        return Task.CompletedTask;
    });

    // WPF EmiDeskWindow.xaml.cs:831-840, 955-974: missing art never blanks the body,
    // and a partial overlay is disabled for the sitting.
    [Fact]
    public Task MissingArtKeepsBodyAndDisablesAnIncompleteOverlay() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var bodyPath = EmiChains.BodyPath("sway1")!;
        var overPath = EmiChains.OverPath("labcoat", "pet")!;
        var bodyBackup = bodyPath + "." + Guid.NewGuid().ToString("N");
        var overBackup = overPath + "." + Guid.NewGuid().ToString("N");
        var desk = new EmiDeskWindow();
        try
        {
            var body = desk.FindControl<Image>("BodyImage")!;
            var over = desk.FindControl<Image>("OutfitOverImage")!;
            var idle = body.Source;
            File.Move(bodyPath, bodyBackup);
            desk.SetPose("sway1");
            Assert.Same(idle, body.Source);
            desk.SetPose("idle");
            desk.SetOutfit("labcoat");
            Assert.True(over.IsVisible);
            File.Move(overPath, overBackup);
            desk.SetPose("pet");
            Assert.False(over.IsVisible);
            Assert.Null(over.Source);
            File.Move(overBackup, overPath);
            desk.SetPose("idle");
            Assert.False(over.IsVisible);
        }
        finally
        {
            if (File.Exists(bodyBackup)) File.Move(bodyBackup, bodyPath);
            if (File.Exists(overBackup)) File.Move(overBackup, overPath);
            desk.ShutDown();
        }
        return Task.CompletedTask;
    });

    // WPF EmiDeskWindow.xaml.cs:821-850, 885-897 keeps the displayed image if art is missing.
    [Fact]
    public Task OutfitChangeReleasesRetiredImagesAfterReplacingSources() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var desk = new EmiDeskWindow();
        try
        {
            var body = desk.FindControl<Image>("BodyImage")!;
            var over = desk.FindControl<Image>("OutfitOverImage")!;
            var oldIdle = Assert.IsType<Bitmap>(body.Source);
            desk.SetPose("pet");
            var oldPet = Assert.IsType<Bitmap>(body.Source);
            bool liveWhenReplaced = false;
            body.PropertyChanged += (_, e) =>
            {
                if (e.Property == Image.SourceProperty && !ReferenceEquals(body.Source, oldPet))
                    liveWhenReplaced = Record.Exception(() => { _ = oldPet.PixelSize; }) is null;
            };
            desk.SetOutfit("labcoat");
            Assert.True(liveWhenReplaced);
            AssertDisposed(oldIdle);
            AssertDisposed(oldPet);
            AssertArt(EmiChains.BodyPath("pet", "labcoat")!, body);
            var coatBody = Assert.IsType<Bitmap>(body.Source);
            var coatOver = Assert.IsType<Bitmap>(over.Source);
            desk.SetPose("idle");
            var coatIdle = Assert.IsType<Bitmap>(body.Source);
            var coatOverIdle = Assert.IsType<Bitmap>(over.Source);
            desk.SetOutfit("swim");
            AssertDisposed(coatBody);
            AssertDisposed(coatIdle);
            AssertDisposed(coatOver);
            AssertDisposed(coatOverIdle);
            AssertArt(EmiChains.BodyPath("idle", "swim")!, body);
            AssertArt(EmiChains.OverPath("swim", "idle")!, over);
        }
        finally { desk.ShutDown(); }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task MissingBodySurvivesOutfitChangeUntilReplaced(bool closeWithFallback) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var standard = EmiChains.BodyPath("pet")!;
        var dressed = EmiChains.BodyPath("pet", "labcoat")!;
        var standardBackup = standard + "." + Guid.NewGuid().ToString("N");
        var dressedBackup = dressed + "." + Guid.NewGuid().ToString("N");
        var desk = new EmiDeskWindow();
        try
        {
            var body = desk.FindControl<Image>("BodyImage")!;
            desk.SetPose("pet");
            var fallback = Assert.IsType<Bitmap>(body.Source);
            File.Move(standard, standardBackup);
            File.Move(dressed, dressedBackup);
            desk.SetOutfit("labcoat");
            Assert.Same(fallback, body.Source);
            Assert.Equal(new PixelSize(859, 869), fallback.PixelSize);
            if (closeWithFallback) { desk.ShutDown(); Assert.Null(body.Source); }
            else { desk.SetPose("idle"); Assert.NotSame(fallback, body.Source); }
            AssertDisposed(fallback);
        }
        finally
        {
            if (File.Exists(standardBackup)) File.Move(standardBackup, standard);
            if (File.Exists(dressedBackup)) File.Move(dressedBackup, dressed);
            desk.ShutDown();
        }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task ClosingReleasesDisplayedAndCachedImages(bool closeShownWindow) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var desk = new EmiDeskWindow();
        var body = desk.FindControl<Image>("BodyImage")!;
        var over = desk.FindControl<Image>("OutfitOverImage")!;
        try
        {
            desk.SetOutfit("labcoat");
            var idle = Assert.IsType<Bitmap>(body.Source);
            var idleOver = Assert.IsType<Bitmap>(over.Source);
            desk.SetPose("pet");
            var pet = Assert.IsType<Bitmap>(body.Source);
            var petOver = Assert.IsType<Bitmap>(over.Source);
            if (closeShownWindow) { desk.Show(); desk.Close(); }
            else desk.ShutDown();
            Assert.Null(body.Source);
            Assert.Null(over.Source);
            AssertDisposed(idle);
            AssertDisposed(idleOver);
            AssertDisposed(pet);
            AssertDisposed(petOver);
        }
        finally { desk.ShutDown(); }
        return Task.CompletedTask;
    });

    private static void AssertDisposed(Bitmap bitmap) =>
        Assert.Throws<ObjectDisposedException>(() => { _ = bitmap.PixelSize; });

    private static void AssertArt(string path, Image image)
    {
        Assert.True(File.Exists(path), path);
        using var expected = new Bitmap(path);
        using var a = new MemoryStream();
        using var b = new MemoryStream();
        expected.Save(a);
        Assert.IsType<Bitmap>(image.Source).Save(b);
        Assert.Equal(a.ToArray(), b.ToArray());
    }

    private static void Setup()
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }
}
