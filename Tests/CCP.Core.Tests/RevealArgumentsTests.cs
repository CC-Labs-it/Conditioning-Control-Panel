using System;
using System.IO;
using ConditioningControlPanel.Helpers;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>win-media-history: the select-the-file command lines (one copy, both heads) and the
/// broken-clip rule the Avalonia head uses in place of WPF's MediaFailed.</summary>
public sealed class RevealArgumentsTests
{
    [Fact]
    public void Explorer_keeps_the_select_switch_outside_the_quotes_and_refuses_a_quote()
    {
        // #1108: quoting "/select,<path>" as one token sent Explorer to Documents.
        Assert.Equal("/select,\"D:\\Conditioning Control Panel\\a.gif\"", RevealArguments.ExplorerSelect(@"D:\Conditioning Control Panel\a.gif"));
        Assert.Equal("\"D:\\Conditioning Control Panel\"", RevealArguments.ExplorerFolder(@"D:\Conditioning Control Panel"));
        Assert.Null(RevealArguments.ExplorerSelect("D:\\weird\"path\\a.gif"));
        Assert.Null(RevealArguments.ExplorerFolder("   "));
    }

    [Fact]
    public void Freedesktop_ShowItems_sends_one_escaped_file_uri()
    {
        var args = RevealArguments.FreedesktopShowItems("/home/me/My Media/a,b.png");
        Assert.Contains("--print-reply", args);   // a desktop without FileManager1 exits non-zero -> folder fallback
        Assert.Equal("org.freedesktop.FileManager1.ShowItems", args[5]);
        // dbus-send splits array:string: on commas, so the comma must not reach it raw.
        Assert.Equal("array:string:file:///home/me/My%20Media/a%2Cb.png", args[6]);
        Assert.Equal("string:", args[7]);
    }

    [Fact]
    public void VideoLooksBroken_only_for_clips_that_clearly_cannot_play()
    {
        var dir = Directory.CreateTempSubdirectory("ccp-reveal-").FullName;
        try
        {
            string Make(string name, byte[] head) { var p = Path.Combine(dir, name); File.WriteAllBytes(p, head); return p; }
            var ftyp = new byte[] { 0, 0, 0, 0x18, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'i', (byte)'s', (byte)'o', (byte)'m', 0, 0, 0, 0 };

            Assert.True(MediaHistoryPreviewRules.VideoLooksBroken(Make("empty.mp4", Array.Empty<byte>())));
            Assert.True(MediaHistoryPreviewRules.VideoLooksBroken(Make("text.mp4", "not a video at all"u8.ToArray())));
            Assert.True(MediaHistoryPreviewRules.VideoLooksBroken(Make("png.webm", new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })));
            Assert.False(MediaHistoryPreviewRules.VideoLooksBroken(Make("real.mp4", ftyp)));
            Assert.False(MediaHistoryPreviewRules.VideoLooksBroken(Make("mp4-named.mkv", ftyp)));     // the bytes decide, not the name
            // Formats the sniffer cannot name are left to the player.
            Assert.False(MediaHistoryPreviewRules.VideoLooksBroken(Make("asf.wmv", new byte[] { 0x30, 0x26, 0xB2, 0x75, 0x8E, 0x66, 0xCF, 0x11 })));
            Assert.False(MediaHistoryPreviewRules.VideoLooksBroken(Make("old.mov", "\0\0\0\x08moov"u8.ToArray())));
            Assert.False(MediaHistoryPreviewRules.VideoLooksBroken(Path.Combine(dir, "gone.mp4")));  // the plan's "file not found"
        }
        finally { Directory.Delete(dir, true); }
    }
}
