using System;
using System.IO;
using System.Text.RegularExpressions;
using ConditioningControlPanel.Services.UI;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// ccp-bugs #1698: the lock card (and the bubble count answer box) looked focused but the keys kept
/// going to the app the player was in, because a plain SetForegroundWindow / Activate fails silently
/// while another process owns the foreground. The show paths now go through <see cref="ForegroundGrab"/>,
/// which borrows the foreground thread's input state and re-checks once the show has settled.
/// </summary>
public class ForegroundGrabTests
{
    private static readonly IntPtr Card = new(0x1234);
    private static readonly IntPtr Discord = new(0x9999);

    [Fact]
    public void CardAlreadyForeground_DoesNothing()
        => Assert.Equal(ForegroundGrab.Step.AlreadyOwned, ForegroundGrab.Plan(Card, Card, 7, 7));

    [Fact]
    public void OtherProcessHoldsKeyboard_BorrowsItsInput()
        => Assert.Equal(ForegroundGrab.Step.BorrowInput, ForegroundGrab.Plan(Discord, Card, 42, 7));

    [Fact]
    public void OurOwnThreadHoldsKeyboard_PlainCallIsEnough()
        => Assert.Equal(ForegroundGrab.Step.Direct, ForegroundGrab.Plan(new IntPtr(0x5555), Card, 7, 7));

    [Fact]
    public void NoForegroundWindow_PlainCallIsEnough()
        => Assert.Equal(ForegroundGrab.Step.Direct, ForegroundGrab.Plan(IntPtr.Zero, Card, 0, 7));

    [Fact]
    public void ZeroTarget_NeverReadsAsOwned()
        => Assert.NotEqual(ForegroundGrab.Step.AlreadyOwned, ForegroundGrab.Plan(IntPtr.Zero, IntPtr.Zero, 0, 7));

    [Fact]
    public void ZeroHandle_TakeRefuses()
        => Assert.False(ForegroundGrab.Take(IntPtr.Zero));

    // ---- source pins: the show paths must not fall back to the silent plain call ----

    private static string AppDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return Path.Combine(dir!.FullName, "ConditioningControlPanel");
    }

    private static string MethodBody(string source, string signature)
    {
        int start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, "missing " + signature);
        int brace = source.IndexOf('{', start);
        int depth = 0;
        for (int i = brace; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0) return source.Substring(brace, i - brace + 1);
        }
        throw new InvalidOperationException("unbalanced " + signature);
    }

    [Theory]
    [InlineData("private void OnShown()")]
    [InlineData("private void PromoteToPrimary()")]
    [InlineData("private void TakeKeyboard()")]
    public void LockCardShowPaths_TakeTheRealForeground(string signature)
    {
        var src = File.ReadAllText(Path.Combine(AppDir(), "Windows", "LockCardWindow.xaml.cs"));
        var body = MethodBody(src, signature);
        Assert.Contains("ForegroundGrab.Take(", body);
        Assert.DoesNotMatch(new Regex(@"(?<!\.)\bSetForegroundWindow\("), body);
    }

    [Fact]
    public void LockCardShow_RechecksAfterTheShowSettles()
    {
        var src = File.ReadAllText(Path.Combine(AppDir(), "Windows", "LockCardWindow.xaml.cs"));
        Assert.Contains("ForegroundGrab.RecheckSoon(", MethodBody(src, "private void TakeKeyboard()"));
        Assert.Contains("primaryWindow?.TakeKeyboard();", MethodBody(src, "public static void ShowOnAllMonitors("));
    }

    [Fact]
    public void BubbleCountAnswer_TakesTheRealForeground()
    {
        var src = File.ReadAllText(Path.Combine(AppDir(), "Windows", "BubbleCountResultWindow.xaml.cs"));
        Assert.Contains("primaryWindow.TakeKeyboard();", MethodBody(src, "public static void ShowOnAllMonitors("));
        var body = MethodBody(src, "private void TakeKeyboard()");
        Assert.Contains("ForegroundGrab.Take(", body);
        Assert.Contains("ForegroundGrab.RecheckSoon(", body);
    }
}
