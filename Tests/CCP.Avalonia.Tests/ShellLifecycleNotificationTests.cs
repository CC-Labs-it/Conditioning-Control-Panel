using System.Collections.Generic;
using System.IO;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>wire-shell-notifications-toasts-b: companion level-up balloons (WPF MainWindow.Companion.cs:48) and the
/// empty-library check behind WarnIf*LibraryEmpty (WPF MainWindow.StartStop.cs:180).</summary>
public sealed class ShellLifecycleNotificationTests
{
    private static List<(string Title, string Body)> Capture(System.Action act)
    {
        var seen = new List<(string, string)>();
        var old = OsNotifications.Sink;
        OsNotifications.Sink = (t, b) => seen.Add((t, b));
        try { act(); } finally { OsNotifications.Sink = old; }
        return seen;
    }

    [Fact]
    public void CompanionLevelUp_notifies_at_max_and_every_tenth_level_only()
    {
        var name = CompanionDefinition.GetById(CompanionId.CultBunny).Name;
        Assert.Equal(new[] { ("MAX LEVEL!", $"{name} has reached maximum level!") },
            Capture(() => MainShellWindow.NotifyCompanionLevelUp(CompanionId.CultBunny, CompanionProgress.MaxLevel)));
        Assert.Equal(new[] { ("Companion Level Up!", $"{name} reached Level 20!") },
            Capture(() => MainShellWindow.NotifyCompanionLevelUp(CompanionId.CultBunny, 20)));
        Assert.Empty(Capture(() => MainShellWindow.NotifyCompanionLevelUp(CompanionId.CultBunny, 7)));
    }

    [Fact]
    public void FolderHasAnyMedia_matches_nested_extensions_case_insensitively()
    {
        var dir = Directory.CreateTempSubdirectory("ccp-media-").FullName;
        try
        {
            string[] exts = { ".png", ".jpg" };
            Assert.False(MainShellWindow.FolderHasAnyMedia(Path.Combine(dir, "missing"), exts));
            Assert.False(MainShellWindow.FolderHasAnyMedia(dir, exts));
            File.WriteAllText(Path.Combine(dir, "notes.txt"), "x");
            Assert.False(MainShellWindow.FolderHasAnyMedia(dir, exts));
            Directory.CreateDirectory(Path.Combine(dir, "sub"));
            File.WriteAllText(Path.Combine(dir, "sub", "a.PNG"), "x");
            Assert.True(MainShellWindow.FolderHasAnyMedia(dir, exts));
        }
        finally { Directory.Delete(dir, true); }
    }
}
