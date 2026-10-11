using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF behaviour finding 3 (evidence/wpf-behaviour, UIA dump run 38149561027): 55 buttons
/// whose content is a panel or a TextBlock exposed their CLR type name ("Avalonia.Controls.StackPanel")
/// as the accessible name, where WPF reads their text. Walks every door and asserts no automation
/// name of a content control is a type name (ButtonAccessibleName, installed by App).</summary>
public sealed class ButtonAccessibleNameTests
{
    private static readonly Regex TypeName = new(@"^(Avalonia|ConditioningControlPanel|System)(\.[\w`]+)+$");

    private static readonly string[] Tabs =
    {
        "settings", "studio", "presets", "haptics", "companion", "awareness", "play", "gradedintake",
        "blinktrainer", "remotecontrol", "discord", "quests", "achievements", "leaderboard", "assets", "appsettings",
    };

    [Fact]
    public Task NoShellButtonIsNamedByItsContentType() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        LocalizationManager.Instance.SetLanguage("en");
        var w = new MainShellWindow();
        w.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var bad = new SortedSet<string>();
            var seen = 0;
            foreach (var tab in Tabs)
            {
                w.ShowTab(tab);
                Dispatcher.UIThread.RunJobs();
                foreach (var c in w.GetVisualDescendants().OfType<ContentControl>().Where(c => c.IsEffectivelyVisible))
                {
                    if (c.Content is not Control) continue;
                    seen++;
                    var name = ControlAutomationPeer.CreatePeerForElement(c).GetName();
                    if (TypeName.IsMatch(name)) bad.Add($"{tab}: {c.GetType().Name} '{c.Name}' -> {name}");
                }
            }
            Assert.True(seen > 100, $"walked only {seen} content controls");
            Assert.True(bad.Count == 0, $"{bad.Count} named by type:\n" + string.Join("\n", bad));
        }
        finally { w.Close(); }
        return Task.CompletedTask;
    });
}
