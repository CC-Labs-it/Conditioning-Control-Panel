using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ConditioningControlPanel.Services.Fyp.Online;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The online-media consent question (2026-11-27): every picture door said "Use Reddit media?" long after
/// the fetch moved to Scrolller, and "Nothing is saved to your disk" after the device cache shipped. The
/// words now live once in <see cref="RemoteMediaConsentCopy"/>; these pin what they say and that the four
/// doors (Library tab, launcher media dialog, Racing Thoughts 3D, Terms &amp; Conditioning) read them
/// instead of a private copy.
/// </summary>
public class RemoteMediaConsentCopyTests
{
    private static readonly Regex RedditWord = new(@"\bReddit\b", RegexOptions.IgnoreCase);

    private static readonly string[] Doors =
    {
        Path.Combine("ConditioningControlPanel", "MainWindow", "MainWindow.Assets.cs"),
        Path.Combine("ConditioningControlPanel", "Windows", "Launcher", "LauncherMediaDialog.xaml.cs"),
        Path.Combine("ConditioningControlPanel", "Services", "Glassroad", "GlassroadHostService.Pictures.cs"),
        Path.Combine("ConditioningControlPanel", "Services", "Tc", "TcHostService.Media.cs"),
    };

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }

    [Fact]
    public void The_question_names_Scrolller_as_the_source()
    {
        Assert.Equal("Use Scrolller media?", RemoteMediaConsentCopy.Title);
        Assert.StartsWith("Pull media from Scrolller?", RemoteMediaConsentCopy.Message, StringComparison.Ordinal);
        Assert.DoesNotMatch(RedditWord, RemoteMediaConsentCopy.Title);
        Assert.DoesNotMatch(RedditWord, RemoteMediaConsentCopy.Message);
    }

    [Fact]
    public void The_question_keeps_its_meaning_and_its_honesty()
    {
        var m = RemoteMediaConsentCopy.Message;
        Assert.Contains("adult content", m, StringComparison.Ordinal);
        Assert.Contains("none of it goes through our servers", m, StringComparison.Ordinal);
        Assert.Contains("Turn it on?", m, StringComparison.Ordinal);
        // The device cache keeps fetched files for a while (ScrolllerDiskCache): never claim it does not.
        Assert.DoesNotContain("Nothing is saved", m, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_language_keys_are_unchanged()
    {
        Assert.Equal("title_remote_media_consent", RemoteMediaConsentCopy.TitleKey);
        Assert.Equal("msg_remote_media_consent", RemoteMediaConsentCopy.MessageKey);
    }

    [Fact]
    public void Every_picture_door_asks_with_the_shared_words()
    {
        var root = RepoRoot();
        var problems = new List<string>();
        foreach (var rel in Doors)
        {
            var text = File.ReadAllText(Path.Combine(root, rel));
            if (!text.Contains("RemoteMediaConsentCopy.Message", StringComparison.Ordinal)
                || !text.Contains("RemoteMediaConsentCopy.Title", StringComparison.Ordinal))
                problems.Add(rel + " does not read RemoteMediaConsentCopy");
        }

        var appDir = Path.Combine(root, "ConditioningControlPanel");
        foreach (var file in Directory.EnumerateFiles(appDir, "*.cs", SearchOption.AllDirectories)
                     .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                              && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)))
        {
            var text = File.ReadAllText(file);
            if (text.Contains("Use Reddit media?", StringComparison.Ordinal)
                || text.Contains("Pull media from Reddit?", StringComparison.Ordinal))
                problems.Add(Path.GetRelativePath(root, file) + " still carries the old Reddit question");
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }
}
