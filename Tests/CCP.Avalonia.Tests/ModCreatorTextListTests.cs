using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.LogicalTree;
using Avalonia.Interactivity;
using ConditioningControlPanel.Localization;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ModCreatorTextListTests
{
    [Fact]
    public void CatalogSearchAndExportKeepOriginalTexts()
    {
        var entries = ModTextCatalog.Build();
        Assert.NotEmpty(entries);
        Assert.Contains(entries, e => e.Text == "BASIC BIMBO" && e.Group == ModTextCatalog.Levels);
        Assert.Single(entries.Where(e => e.Text == "BASIC BIMBO"));
        Assert.Contains(ModTextCatalog.Search(entries, "basic bimbo"), e => e.Text == "BASIC BIMBO");
        Assert.Empty(ModTextCatalog.Search(entries, "zzzz-not-a-text"));
        Assert.Contains("BASIC BIMBO", ModTextCatalog.ToExportText(entries, null));
    }

    [Fact]
    public void PickUsesExistingThenEmptyThenNewRow() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current == null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia().WithInterFont()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        if (!FontManager.Current.TryGetGlyphTypeface(new Typeface(new FontFamily("fonts:Inter#Inter"), FontStyle.Normal, FontWeight.Bold), out _))
            FontManager.Current.AddFontCollection(new global::Avalonia.Fonts.Inter.InterFontCollection());
        var window = new ModCreatorWindow();
        try
        {
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
            var type = typeof(ModCreatorWindow);
            var rows = (List<(TextBox From, TextBox To)>)type.GetField("_textReplacements", flags)!.GetValue(window)!;
            void Invoke(string method, params object[] args) => type.GetMethod(method, flags)!.Invoke(window, args);
            var search = window.GetLogicalDescendants().OfType<TextBox>().Single(b => b.PlaceholderText == Loc.Get("mod_text_search"));
            search.Text = "basic bimbo";
            Dispatcher.UIThread.RunJobs();
            var pick = window.GetLogicalDescendants().OfType<Button>().Single(b => b.Content is TextBlock t && t.Text?.StartsWith("BASIC BIMBO", StringComparison.Ordinal) == true);
            pick.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Single(rows);
            Assert.Equal("BASIC BIMBO", rows[0].From.Text);
            Invoke("AddReplacementRow", "first", "replacement");
            Invoke("AddReplacementRow", "", "");
            Invoke("PickOriginalText", "first");
            Assert.Equal(3, rows.Count);
            Assert.Equal("replacement", rows[1].To.Text);
            Invoke("PickOriginalText", "second");
            Assert.Equal("second", rows[2].From.Text);
            Invoke("PickOriginalText", "third");
            Assert.Equal(4, rows.Count);
            Assert.Equal("third", rows[3].From.Text);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(rows[3].To.Text?.Length ?? 0, rows[3].To.CaretIndex);
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
    });
}
