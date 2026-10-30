using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using VerifyTests;
using static VerifyXunit.Verifier;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace CCP.Avalonia.Snapshot.Tests;

/// <summary>
/// Locks the look of every parity row that is `verified`/`improved` in docs/avalonia-parity.html:
/// one baseline per row id under Snapshots/. A new baseline is accepted only with the WPF
/// side-by-side image path in the commit message (docs/avalonia-parity.html, "Locking a row").
/// ~/ccp-port/bin/ledger-snapshot-check.sh fails a verified/improved row without a file here.
/// </summary>
public sealed class VerifiedRowSnapshotTests
{
    /// <summary>Row id -> the window that row's WPF side-by-side compared against.</summary>
    private static readonly Dictionary<string, Func<Window>> Rows = new()
    {
        ["win-layered-audio"] = () => new LayeredAudioWindow(),
        ["release-content-mod-manager-rows"] = () => new ModManagerDialog(),
    };

    public static TheoryData<string> RowIds => new(Rows.Keys);

    // SSIM, not byte equality: Skia's PNG encoder and zlib may differ in bytes for identical
    // pixels across distros. 0.995 still fails on one changed margin, text change or colour swap
    // in a 480x660 window (fail-proof in the commit), and passes Arch vs ubuntu-latest AA noise.
    internal const double SsimThreshold = 0.995;

    [ModuleInitializer]
    internal static void Init()
    {
        // Disposable profile before CorePaths is first read; XDG is sandboxed by the linked dispatcher file.
        var profile = Path.Combine(Path.GetTempPath(), "ccp-snapshot-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(profile);
        Environment.SetEnvironmentVariable("CCP_USERDATA_DIR", profile);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => { try { Directory.Delete(profile, true); } catch { } };

        VerifyAvalonia.Initialize();
        VerifierSettings.UseSsimForPng(SsimThreshold);
        // A mismatch must never open a diff tool (meld) on the user's desktop (P74): read .received.png instead.
        DiffEngine.DiffRunner.Disabled = true;
    }

    private static bool _setUp;

    /// <summary>
    /// RenderProof.EnsureSetUp (the --render-all path: app builder, Inter, Skia, real drawing) plus one
    /// pin: emoji fall back to the head's bundled Noto Emoji (AppFonts, systemEmoji off) before fontconfig,
    /// which picked Noto Sans Symbols 2 for the headphones on Arch and another face on ubuntu-latest,
    /// shifting the whole layout (CI run 38052778670).
    /// </summary>
    private static void SetUpOnce()
    {
        if (_setUp) return;
        Program.BuildAvaloniaApp()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .With(AppFonts.Options(systemEmoji: false))
            .SetupWithoutStarting();
        // A missing resource would silently fall through to fontconfig again: fail loudly instead.
        if (!FontManager.Current.TryMatchCharacter(0x1F3A7, FontStyle.Normal, FontWeight.Normal, FontStretch.Normal,
                null, null, out var face) || face.FontFamily.Name != "Noto Emoji")
            throw new InvalidOperationException("snapshot fallback font is not the bundled Noto Emoji");
        _setUp = true;
    }

    /// <summary>Distro-check F1/F4: the default family is the bundled Inter (a system without fonts
    /// aborted), "Fredoka, Segoe UI" draws the bundled Fredoka, and emoji have a bundled fallback.</summary>
    [Fact]
    public Task AppFontsNeverDependOnSystemFonts() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        SetUpOnce();
        var fm = FontManager.Current;
        Assert.Equal("Inter", fm.DefaultFontFamily.Name);
        Assert.True(fm.TryGetGlyphTypeface(new Typeface("Fredoka, Segoe UI"), out var face));
        Assert.StartsWith("Fredoka", face!.FamilyName);
        Assert.True(fm.TryGetGlyphTypeface(new Typeface("Consolas, Courier New"), out var mono));
        Assert.Equal("Noto Sans Mono", mono!.FamilyName);
        Assert.True(fm.TryMatchCharacter(0x1F600, FontStyle.Normal, FontWeight.Normal, FontStretch.Normal, null, null, out var emoji));
        Assert.Equal("Noto Emoji", emoji.FontFamily.Name);
        // The app (not the render lock) asks the system's colour emoji font first, through its own collection
        // (SystemEmojiFontCollection: no synthetic bold/italic, render-all-segv); none installed -> the bundled one.
        Assert.Equal(SystemEmojiFontCollection.SystemFamilyName() ?? "Noto Emoji", AppFonts.Options().FontFallbacks![0].FontFamily.Name);
        return Task.CompletedTask;
    });

    [Theory]
    [MemberData(nameof(RowIds))]
    public Task VerifiedRowRenderIsLocked(string rowId) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        RenderProof.Rendering = true;
        SetUpOnce();
        CoreSettings.Current.MotionLevel = MotionLevel.Off;

        var window = Rows[rowId]();
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Dispatcher.UIThread.RunJobs();
            await Verify(window).UseDirectory("Snapshots").UseFileName(rowId);
        }
        finally
        {
            // Verify resumes off the UI thread; close the window back on it.
            await Dispatcher.UIThread.InvokeAsync(window.Close);
        }
    });
}
