using System;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>
/// ccp-bugs #1260's rule, now one copy in CCP.Core: a silent LibVLC player gets
/// <see cref="LibVlcSilence.NoAudioOption"/> on its Media. The WPF suite's LibVlcSilenceTests
/// still scans the WPF head for Mute/Volume writes.
/// </summary>
public sealed class LibVlcSilenceCoreTests
{
    [Fact]
    public void TheSilentOptionIsLibVlcsNoAudio()
    {
        Assert.Equal(":no-audio", LibVlcSilence.NoAudioOption);
    }

    [Fact]
    public void TheAvaloniaHeadSilencesThroughTheCoreRuleNotALiteral()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(root.FullName, "ConditioningControlPanel.sln"))) root = root.Parent!;
        var head = Path.Combine(root.FullName, "CCP.Avalonia");

        var sep = Path.DirectorySeparatorChar;
        var files = Directory.EnumerateFiles(head, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{sep}obj{sep}") && !f.Contains($"{sep}bin{sep}"))
            .ToList();
        Assert.NotEmpty(files);

        var literals = files.Where(f => File.ReadAllText(f).Contains("\":no-audio\""))
                            .Select(Path.GetFileName).ToList();
        Assert.True(literals.Count == 0,
            "Use LibVlcSilence.NoAudioOption instead of a \":no-audio\" literal: " + string.Join(", ", literals));
        Assert.Contains(files, f => File.ReadAllText(f).Contains("LibVlcSilence.NoAudioOption"));
    }
}
