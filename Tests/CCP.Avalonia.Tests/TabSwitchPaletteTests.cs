using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.UI;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class TabSwitchPaletteTests
{
    [Fact]
    public Task SectionPalettePublishesAtomicallyWithOneResourceInvalidation() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var app = Application.Current!;
        MainShellWindow.PaintSectionInk(NavSections.Home);
        var notifications = 0;
        var completeAtNotification = true;
        void Changed(object? sender, ResourcesChangedEventArgs e)
        {
            notifications++;
            completeAtNotification &= Matches("SectionInk", NavStripRules.Ink(NavSections.Play))
                && Matches("SectionTint", NavStripRules.Tint(NavSections.Play))
                && Matches("SectionRule", NavStripRules.Rule(NavSections.Play))
                && Matches("SectionOutline", NavStripRules.Outline(NavSections.Play));
        }
        bool Matches(string key, Color expected) =>
            app.TryFindResource(key, out var color) && color is Color c && c == expected
            && app.TryFindResource(key + "Brush", out var brush) && brush is ISolidColorBrush b && b.Color == expected;
        app.ResourcesChanged += Changed;
        try
        {
            MainShellWindow.PaintSectionInk(NavSections.Play);
            Assert.Equal(1, notifications); // eight global walks used to re-resolve every DynamicResource
            Assert.True(completeAtNotification);
            MainShellWindow.PaintSectionInk(NavSections.Play);
            Assert.Equal(1, notifications); // an unchanged palette must not rebind the shell
        }
        finally { app.ResourcesChanged -= Changed; }
        return Task.CompletedTask;
    });
}
