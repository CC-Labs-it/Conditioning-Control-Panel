using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace CCP.Avalonia.Tests;

[Collection(RunsAloneCollection.Name)]
public sealed class EmiBookPassiveTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    [Fact]
    public Task PassiveRecipeRunsBeforeTheBookIsShown() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var apply = EmiBookWindow.ApplyPassive;
        var bookmark = EmiState.Current.BookCard;
        var book = new EmiBookWindow();
        int applied = 0;
        double widthAtApply = 0;
        var narrow = EmiBookWindow.ForceNarrow;
        // Narrow placement must change Width from the XAML 364, so a PlaceWindow before the
        // recipe (geometry before override-redirect, X11Overlay.SetOverrideRedirect) shows here.
        EmiBookWindow.ForceNarrow = true;
        EmiBookWindow.ApplyPassive = w => { Assert.Same(book, w); Assert.False(w.IsVisible); widthAtApply = w.Width; applied++; };
        try
        {
            book.OpenBook(null);
            Assert.True(book.IsVisible);
            Assert.Equal(1, applied);
            Assert.Equal(364, widthAtApply);
            Assert.Equal(EmiBookLayout.NarrowWidth, book.Width);
            book.OpenBook(null);
            Assert.Equal(1, applied);
        }
        finally { book.Close(); EmiBookWindow.ApplyPassive = apply; EmiBookWindow.ForceNarrow = narrow; EmiState.Current.BookCard = bookmark; }
        return Task.CompletedTask;
    });

}
