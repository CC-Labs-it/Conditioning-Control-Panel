using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Xml.Linq;
using Avalonia.Controls;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class RainHelpTests
{
    private static string Root([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));

    [Fact]
    public void HelpRowUsesSharedReleaseGateAndHasNoCounterDoor()
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
        Assert.DoesNotContain(shell.Descendants(), e => (string?)e.Attribute(x + "Name") == "BtnHelpPrizeCounter");
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
        LocalizationManager.Instance.SetLanguage("en");
        var button = new Button { Name = "BtnHelpRain" };
        try
        {
            MainShellWindow.AttachHelpButton(button);
            Assert.Equal("RainV2", HelpPopover.SectionOf(button));
            var content = MainShellWindow.BuildRainHelpContent();
            Assert.Equal(Loc.Get("help_rain_title"), content.Title);
            Assert.Equal(Loc.Get("help_rain_what"), content.WhatItDoes);
            Assert.Equal(new[] { Loc.Get("help_rain_tip1"), Loc.Get("help_rain_tip2"), Loc.Get("help_rain_tip3") }, content.Tips);
            Assert.Equal(Loc.Get("help_rain_how"), content.HowItWorks);
        }
        finally { HelpPopover.Clear(button); }
        return Task.CompletedTask;
    });
}
