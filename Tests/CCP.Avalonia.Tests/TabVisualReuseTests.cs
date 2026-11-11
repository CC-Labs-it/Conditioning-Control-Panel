using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class TabVisualReuseTests
{
    [Fact]
    public void AssemblyStartsWithProductionFonts() => AvaloniaTestDispatcher.Run(() =>
    {
        Assert.NotNull(Application.Current);
        Assert.Equal("Inter", global::Avalonia.Media.FontManager.Current.DefaultFontFamily.Name);
        Assert.NotNull(new global::Avalonia.Media.Typeface(ConditioningControlPanel.Avalonia.Platform.AppFonts.Inter).GlyphTypeface);
    });

    [Fact]
    public void SectionPillsAreReusedAndTierChangesInvalidateThem() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null) RenderProof.EnsureSetUp();
        var strip = new ConditioningControlPanel.Avalonia.Views.Controls.SectionTabStrip();
        var locked = false;
        strip.PillLocked = _ => locked;
        var section = ConditioningControlPanel.Services.UI.NavSections.SectionForTab("programs");
        strip.Show(section, "programs");
        var pill = strip.PillFor("programs");
        Assert.NotNull(pill);
        strip.Show(ConditioningControlPanel.Services.UI.NavSections.SectionForTab("blinktrainer"), "blinktrainer");
        strip.Show(section, "programs");
        Assert.Same(pill, strip.PillFor("programs"));
        locked = true;
        strip.Show("settings", "appsettings");
        strip.Show(section, "programs");
        Assert.NotSame(pill, strip.PillFor("programs"));
    });

    [Fact]
    public void SectionInkRetainsBrushAndUpdatesColor() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null) RenderProof.EnsureSetUp();
        var app = Application.Current!;
        Assert.True(app.Resources.MergedDictionaries[1].TryGetResource("SectionInkBrush", null, out var themeBrush));
        ConditioningControlPanel.Avalonia.Views.Windows.MainShellWindow.PaintSectionInk("play");
        Assert.True(app.TryFindResource("SectionInkBrush", out var before));
        ConditioningControlPanel.Avalonia.Views.Windows.MainShellWindow.PaintSectionInk("settings");
        Assert.True(app.TryFindResource("SectionInkBrush", out var after));
        Assert.Same(before, after);
        Assert.True(app.TryFindResource("SectionInk", out var color));
        Assert.Equal((global::Avalonia.Media.Color)color!, Assert.IsType<global::Avalonia.Media.SolidColorBrush>(after).Color);
        Assert.Equal(global::Avalonia.Media.Color.Parse("#FFCBB9FC"), Assert.IsType<global::Avalonia.Media.SolidColorBrush>(themeBrush).Color);
    });

    [Fact]
    public void RepeatShowAndUnchangedRefreshKeepRowsAndFolderCards() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null) RenderProof.EnsureSetUp();
        var old = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var directory = Directory.CreateTempSubdirectory("ccp-tab-reuse-");
        Window? window = null;
        try
        {
            var programs = new ProgramsTabView();
            var blink = new BlinkTrainerTabView { IsVisible = false };
            service.Current.BlinkTrainerFolders.Add(directory.FullName);
            window = new Window { Content = new Grid { Children = { programs, blink } } };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var list = programs.FindControl<ListBox>("ProgramLibraryList")!;
            var rows = list.Items.Cast<object>().ToArray();
            var selected = list.SelectedItem;
            var chapters = programs.FindControl<ItemsControl>("ProgramDetailsChapterList")!.ItemsSource;
            var rowChanges = programs.BrowseRowChanges;
            programs.RefreshPrograms(); // session events can request a refresh while browsing
            Assert.Equal(rows, list.Items.Cast<object>().ToArray());
            Assert.Same(selected, list.SelectedItem);
            Assert.Same(chapters, programs.FindControl<ItemsControl>("ProgramDetailsChapterList")!.ItemsSource);
            Assert.Equal(rowChanges, programs.BrowseRowChanges);

            programs.IsVisible = false;
            blink.IsVisible = true;
            var cards = blink.FindControl<StackPanel>("BlinkTrainerFolderCardsHost")!;
            var card = Assert.Single(cards.Children);
            var builds = blink.FolderCardBuilds;
            for (var i = 0; i < 3; i++)
            {
                blink.IsVisible = false;
                programs.IsVisible = true;
                programs.IsVisible = false;
                blink.IsVisible = true;
            }
            Assert.Same(card, Assert.Single(cards.Children));
            Assert.Equal(builds, blink.FolderCardBuilds);
            Assert.Equal(rowChanges, programs.BrowseRowChanges);

            // Files changed outside CCP must still change the displayed count on return.
            blink.IsVisible = false;
            File.WriteAllBytes(Path.Combine(directory.FullName, "new.png"), new byte[] { 1 });
            blink.IsVisible = true;
            Assert.NotSame(card, Assert.Single(cards.Children));
            Assert.Equal(builds + 1, blink.FolderCardBuilds);
            card = Assert.Single(cards.Children);
            var second = directory.CreateSubdirectory("second");
            blink.AddFolder(second.FullName);
            Assert.Same(card, cards.Children[0]);
            Assert.Equal(2, cards.Children.Count);

            // A mod switch or lockdown replaces PinkBrush: the hover border must follow it.
            var accent = global::Avalonia.Media.Brushes.Lime;
            window.Resources["PinkBrush"] = accent;
            blink.IsVisible = false;
            blink.IsVisible = true;
            var recolored = Assert.IsType<Border>(cards.Children[0]);
            Assert.NotSame(card, recolored);
            recolored.RaiseEvent(new global::Avalonia.Input.PointerEventArgs(global::Avalonia.Input.InputElement.PointerEnteredEvent, recolored,
                new global::Avalonia.Input.Pointer(0, global::Avalonia.Input.PointerType.Mouse, true), window, default, 0,
                new global::Avalonia.Input.PointerPointProperties(), global::Avalonia.Input.KeyModifiers.None));
            Assert.Same(accent, recolored.BorderBrush);

            // Saved settings may list a folder twice; each entry gets its own card on every refresh.
            service.Current.BlinkTrainerFolders.Add(second.FullName);
            blink.IsVisible = false;
            blink.IsVisible = true;
            blink.IsVisible = false;
            blink.IsVisible = true;
            Assert.Equal(3, cards.Children.Count);
            Assert.Equal(3, cards.Children.Distinct().Count());
        }
        finally
        {
            window?.Close();
            CoreSettings.ServiceProvider = old;
            directory.Delete(true);
        }
    });
}
