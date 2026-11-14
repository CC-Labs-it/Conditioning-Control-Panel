using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using Xunit;
using IconKind = FluentIcons.Common.Symbol;

namespace CCP.Avalonia.Tests;

public sealed class RainHelpTests
{
    private static string Root([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));

    [Fact]
    public void HelpRowUsesSharedReleaseGate()
    {
        var shell = XDocument.Load(Path.Combine(Root(), "CCP.Avalonia/Views/Windows/MainShellWindow.axaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var row = shell.Descendants().Single(e => (string?)e.Attribute(x + "Name") == "HelpRainRow");
        Assert.Equal("{x:Static companion:RainV2Gate.On}", (string?)row.Attribute("IsVisible"));
        Assert.Equal("clr-namespace:ConditioningControlPanel.Services.Companion;assembly=CCP.Core",
            shell.Root!.GetNamespaceOfPrefix("companion")!.NamespaceName);
        var button = row.Descendants().Single(e => (string?)e.Attribute(x + "Name") == "BtnHelpRain");
        Assert.Equal("{loc:Str help_read_btn}", (string?)button.Attribute("Content"));
        Assert.Null(button.Attribute("Click"));
        var icon = row.Descendants().Single(e => e.Name.LocalName == "IconGlyph");
        Assert.Equal("Wand", (string?)icon.Attribute("Kind"));
        // The Prize Counter row is its own (b103 landed the counter, restack b218 #4411): it sits before the gated Rain row, ungated.
        var rows = shell.Descendants().Where(e => e.Name.LocalName == "Border").ToList();
        var counter = rows.Single(e => e.Descendants().Any(d => (string?)d.Attribute(x + "Name") == "BtnHelpPrizeCounter") && e.Descendants().Count(d => d.Name.LocalName == "Border") == 0);
        Assert.Null(counter.Attribute("IsVisible"));
        Assert.True(rows.IndexOf(counter) < rows.IndexOf(row));
    }

    [Fact]
    public void EveryLanguageHasTheRainPageAndCompanionTitle()
    {
        var files = Directory.GetFiles(Path.Combine(Root(), "CCP.Core/Localization/Languages"), "*.json");
        Assert.Equal(10, files.Length);
        foreach (var file in files)
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            foreach (var suffix in new[] { "title", "sub", "what", "tip1", "tip2", "tip3", "how" })
                Assert.False(string.IsNullOrWhiteSpace(doc.RootElement.GetProperty("help_rain_" + suffix).GetString()));
            Assert.Contains("{companion}", doc.RootElement.GetProperty("help_rain_title").GetString());
            Assert.False(string.IsNullOrWhiteSpace(doc.RootElement.GetProperty("help_read_btn").GetString()));
        }
    }

    [Fact]
    public Task ReadAttachesLocalizedRainPage() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .WithInterFont()
                .With(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Options())
                .ConfigureFonts(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Register)
                .SetupWithoutStarting();
        LocalizationManager.Instance.SetLanguage("en");
        var button = new Button { Name = "BtnHelpRain" };
        var host = new Window { Content = button };
        try
        {
            host.Show();
            MainShellWindow.AttachHelpButton(button);
            Assert.Equal("RainV2", HelpPopover.SectionOf(button));
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.True(HelpPopover.IsOpen(button));
            var icon = Assert.Single(HelpPopover.PopupContent(button)!.GetVisualDescendants()
                .OfType<IconGlyph>().Where(g => g.Kind == IconKind.Wand));
            Assert.False(icon.IsFallback);
            var content = MainShellWindow.BuildRainHelpContent();
            Assert.Equal(Loc.Get("help_rain_title"), content.Title);
            Assert.Equal(Loc.Get("help_rain_what"), content.WhatItDoes);
            Assert.Equal(new[] { Loc.Get("help_rain_tip1"), Loc.Get("help_rain_tip2"), Loc.Get("help_rain_tip3") }, content.Tips);
            Assert.Equal(Loc.Get("help_rain_how"), content.HowItWorks);
        }
        finally { HelpPopover.Clear(button); host.Close(); }
        return Task.CompletedTask;
    });
}
