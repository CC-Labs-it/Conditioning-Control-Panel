using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Platform.Storage;
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

[Collection(RunsAloneCollection.Name)]
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
            var list = window.GetLogicalDescendants().OfType<ListBox>().Single(b => b.Items.OfType<ListBoxItem>().Any(i => i.Tag is ModTextEntry e && e.Text == "BASIC BIMBO"));
            list.SelectedItem = list.Items.OfType<ListBoxItem>().Single(i => i.Tag is ModTextEntry e && e.Text == "BASIC BIMBO");
            list.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
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
    [Fact]
    public void MetadataIsLocalizedInEveryLanguage()
    {
        var previous = LocalizationManager.Instance.CurrentLanguage;
        try
        {
            var entries = ModTextCatalog.Build();
            foreach (var language in new[] { "en", "de", "es", "fr", "it", "ja", "ko", "pt-BR", "ru", "zh-CN" })
            {
                LocalizationManager.Instance.SetLanguage(language);
                foreach (var entry in entries)
                {
                    Assert.DoesNotContain("mod_text_", ModCreatorWindow.LocalizeTextGroup(entry.Group));
                    Assert.DoesNotContain("mod_text_", ModCreatorWindow.LocalizeTextSource(entry.Source));
                }
                Assert.DoesNotContain("mod_text_", ModCreatorWindow.LocalizeTextGroup(ModTextCatalog.SkillGroup("Flash")));
                Assert.Contains("Flash", ModCreatorWindow.LocalizeTextGroup(ModTextCatalog.SkillGroup("Flash")));
                Assert.DoesNotContain("mod_text_", Loc.GetF("mod_text_export_success", 2, "test.txt"));
            }
            LocalizationManager.Instance.SetLanguage("fr");
            Assert.Equal("Compétences", ModCreatorWindow.LocalizeTextGroup("Skills"));
            Assert.Equal("effet de compétence", ModCreatorWindow.LocalizeTextSource("skill effect"));
            Assert.Contains("20", ModCreatorWindow.LocalizeTextSource("rank title, level 20+"));
        }
        finally { LocalizationManager.Instance.SetLanguage(previous); }
    }

    [Fact]
    public void ArrowKeysNavigateWithoutPickingAndEnterPicks() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current == null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia().WithInterFont()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var window = new ModCreatorWindow();
        try
        {
            window.Show();
            typeof(ModCreatorWindow).GetMethod("NavigateToSection", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, new object[] { "replacements" });
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var list = window.GetLogicalDescendants().OfType<ListBox>().Single(b => b.Items.OfType<ListBoxItem>().Any(i => i.Tag is ModTextEntry));
            var items = list.Items.OfType<ListBoxItem>().Where(i => i.Tag is ModTextEntry).Take(2).ToArray();
            items[0].BringIntoView(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var point = items[0].TranslatePoint(new Point(10, items[0].Bounds.Height / 2), window)!.Value;
            window.MouseMove(point); window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            var rows = (List<(TextBox From, TextBox To)>)typeof(ModCreatorWindow).GetField("_textReplacements", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
            Assert.Single(rows);
            Assert.Equal(((ModTextEntry)items[0].Tag!).Text, rows[0].From.Text);
            list.SelectedItem = items[0]; items[0].Focus();
            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, "");
            Dispatcher.UIThread.RunJobs();
            Assert.Same(items[1], list.SelectedItem);
            Assert.Single(rows);
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, rows.Count);
            Assert.Equal(((ModTextEntry)items[1].Tag!).Text, rows[1].From.Text);
            Assert.True(rows[1].To.IsFocused);
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
    });

    public class ExportFile : DispatchProxy
    {
        public string PathValue = "";
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name switch
        {
            "get_Name" => Path.GetFileName(PathValue),
            "OpenWriteAsync" => Task.FromResult<Stream>(File.Create(PathValue)),
            "Dispose" => null,
            _ => throw new NotSupportedException(method?.Name)
        };
    }

    [Fact]
    public Task ExportWritesBeforeReportingCountAndFilename() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current == null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia().WithInterFont()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var path = Path.Combine(Path.GetTempPath(), "ccp-text-" + Guid.NewGuid() + ".txt");
        var file = DispatchProxy.Create<IStorageFile, ExportFile>();
        ((ExportFile)(object)file).PathValue = path;
        var window = new ModCreatorWindow();
        try
        {
            var entries = new[] { new ModTextEntry(ModTextCatalog.Other, "Example", "feature label") };
            await window.WriteOriginalTexts(file, entries);
            Assert.Contains("Example", File.ReadAllText(path));
            var status = (TextBlock)typeof(ModCreatorWindow).GetField("_txtStatus", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
            Assert.Equal(Loc.GetF("mod_text_export_success", 1, Path.GetFileName(path)), status.Text);
        }
        finally { window.Close(); if (File.Exists(path)) File.Delete(path); }
    });
}
