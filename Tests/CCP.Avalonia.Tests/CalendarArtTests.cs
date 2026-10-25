using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using SkiaSharp;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls.Billboard;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Billboard;
using ConditioningControlPanel.Services.Billboard.Providers;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class CalendarArtTests
{
    [Fact]
    public void HomeCardUsesCalendarArtAndPanicParksItsClock() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .WithInterFont().With(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Options())
                .ConfigureFonts(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Register)
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var oldSettings = CoreSettings.ServiceProvider;
        var oldMake = MainShellWindow.MakeShowcase;
        var oldMotion = CalendarArtView.MotionAllowed;
        CoreSettings.ServiceProvider = () => settings;
        MainShellWindow? shell = null;
        try
        {
            CoreSettings.Current.DashboardBrowserCollapsed = true;
            CoreSettings.Current.MotionLevel = MotionLevel.Off;
            var card = EventCards.Locktober(true, new DateTime(2026, 10, 25),
                new RaffleReading(2, 3600, new[] { 2, 9 }), key => key)!;
            MainShellWindow.MakeShowcase = () => new Provider(card);
            shell = new MainShellWindow();
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            var host = shell.BillboardHost!;
            for (int i = 0; i < host.Deck.Cards.Count && host.CurrentCard?.Spec.Id != card.Id; i++) host.Advance();
            var art = Assert.Single(host.GetLogicalDescendants().OfType<CalendarArtView>());
            Assert.True(art.Data.IsLit(9));
            Assert.False(art.Data.IsLit(1));
            Assert.False(art.Running);
            using var bitmap = new RenderTargetBitmap(new PixelSize(800, 300));
            art.Measure(new Size(800, 300));
            art.Arrange(new Rect(0, 0, 800, 300));
            bitmap.Render(art); // exercises the actual drawing code, including text and progress
            // Landed frames reuse their number layouts instead of building ~31 per tick.
            int built = art.TextLayoutsBuilt;
            Assert.True(built > 0);
            bitmap.Render(art);
            Assert.Equal(built, art.TextLayoutsBuilt);
            using var pixels = new MemoryStream();
            bitmap.Save(pixels);
            pixels.Position = 0;
            using var decoded = SKBitmap.Decode(pixels);
            // Day 2 is counted, day 1 is not; compare inside their faces, above the numbers.
            Assert.True(decoded.GetPixel(443, 49).Red > decoded.GetPixel(405, 49).Red + 50);
            CalendarArtView.MotionAllowed = () => true;
            art.Run();
            Assert.True(art.Running);
            art.Hold();
            Assert.False(art.Running);
            art.Run();
            Assert.True(art.Running);
            var retired = new CalendarArtView(art.Data);
            retired.Run();
            retired.Release();
            retired.Run();
            Assert.False(retired.Running);
            PanicSurfaces.All.Single(s => s.Id == "billboard-calendar").Stop(shell);
            Assert.False(art.Running);
            art.Run(); // panic parks, it does not freeze a single-card deck for the session
            Assert.True(art.Running);
            art.Release();
            // A hold before the first Run (Begin runs before the shell shows) keeps the counted-day pop.
            var early = new CalendarArtView(art.Data);
            early.Hold();
            Assert.Equal(0, early.Offset);
            early.Release();
        }
        finally
        {
            shell?.Close();
            MainShellWindow.MakeShowcase = oldMake;
            CalendarArtView.MotionAllowed = oldMotion;
            CoreSettings.ServiceProvider = oldSettings;
        }
    });

    private readonly SettingsService settings = new();
    private sealed class Provider(BillboardCardSpec card) : IBillboardProvider
    {
        public string Id => "calendar-test";
        public IEnumerable<BillboardCardSpec> Current(BillboardContext context) => new[] { card };
        public void Invoke(string target) { }
        public event EventHandler? Changed { add { } remove { } }
    }
}
