using Avalonia.Input.Platform;
using System;
using System.Reflection;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;
using SafeLink = ConditioningControlPanel.Avalonia.Controls.SafeHyperlinkButton;

namespace CCP.Avalonia.Tests;

public sealed class BannerMotionTests
{
    [Fact]
    public Task OpacityTransitionsFollowMotionWithoutOpeningAWindow() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var original = CoreSettings.Current.MotionLevel;
        try
        {
            var beat = new TextBlock();
            var ensure = typeof(MainShellWindow).GetMethod("EnsureFadeTransition", BindingFlags.Static | BindingFlags.NonPublic)!;
            CoreSettings.Current.MotionLevel = MotionLevel.Reduced;
            ensure.Invoke(null, new object[] { beat });
            Assert.Single(beat.Transitions!);
            CoreSettings.Current.MotionLevel = MotionLevel.Off;
            ensure.Invoke(null, new object[] { beat });
            Assert.Null(beat.Transitions);
        }
        finally { CoreSettings.Current.MotionLevel = original; }
        return Task.CompletedTask;
    });

    [Fact]
    public Task MotionOffSwapsWithoutAnOpacityTransition() => InShell(w =>
    {
        CoreSettings.Current.MotionLevel = MotionLevel.Off;
        Tick(w);
        var secondary = w.Named<TextBlock>("TxtBannerSecondary")!;
        Assert.True(secondary.Transitions is null || secondary.Transitions.Count == 0);
        Assert.Equal(1, secondary.Opacity);
        Assert.True(secondary.IsHitTestVisible);
        Assert.False(w.Named<TextBlock>("TxtBannerPrimary")!.IsHitTestVisible);
    });

    [Fact]
    public Task BannerUsesWpfDimensions() => InShell(w =>
    {
        var host = w.Named<Border>("HeaderBannerHost")!;
        Assert.Equal(31, host.Height);
        Assert.Equal(744, host.MaxWidth);
        Assert.Equal(14, w.Named<TextBlock>("TxtBannerPrimary")!.FontSize);
    });

    [Fact]
    public Task SupportLinkUsesTheGuardedOpener() => InShell(w =>
    {
        var link = Assert.IsType<SafeLink>(w.Named<SafeLink>("BannerSupportLink"));
        var shell = ConditioningControlPanel.Avalonia.Platform.ExternalOpener.Shell;
        string? target = null;
        try
        {
            ConditioningControlPanel.Avalonia.Platform.ExternalOpener.Shell = url => { target = url; return true; };
            // The sandbox must still refuse the click before it reaches the shell.
            typeof(SafeLink).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(link, null);
            Assert.Equal("https://linktr.ee/CodeBambi", link.NavigateUri!.AbsoluteUri);
            Assert.Null(target);
        }
        finally { ConditioningControlPanel.Avalonia.Platform.ExternalOpener.Shell = shell; }
    });

    [Theory]
    [InlineData("unified")]
    [InlineData("provider")]
    [InlineData("signed-in")]
    public Task WelcomeFollowsIdentityChangesAndProviderFallback(string source) => InShell(w =>
    {
        var s = CoreSettings.Current;
        s.OfflineMode = false;
        var property = typeof(ConditioningControlPanel.Avalonia.Platform.AccountSeed).GetProperty("Patreon", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = property.GetValue(null);
        using var provider = new ConditioningControlPanel.Services.ProviderSubscription("patreon");
        try
        {
            property.SetValue(null, provider);
            s.UserDisplayName = source == "unified" ? "Bambi" : " ";
            provider.DisplayName = source == "provider" ? "Provider" : null;
            s.UnifiedId = "signed-in";
            w.UpdateQuickLoginUI(accountChanged: true);
            var expected = source == "signed-in"
                ? ConditioningControlPanel.Localization.Loc.Get("banner_signed_in_no_name")
                : ConditioningControlPanel.Localization.Loc.GetF("label_welcome_back_0", source == "unified" ? "Bambi" : "Provider");
            Assert.Equal(expected, w.Named<TextBlock>("TxtBannerSecondary")!.Text);
        }
        finally { property.SetValue(null, previous); }
    });

    [Fact]
    public Task WebFailureCopiesTheLinkAndPanicClosesThePrompt() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var refused = SafeLink.Refused;
        Task? open = null;
        Task<string?>? clipboard = null;
        try
        {
            // A browser failure, not a sandbox refusal: only then does WPF's BrowserLauncher fallback apply.
            SafeLink.Refused = _ => false;
            await InShell(w =>
            {
                var link = w.Named<SafeLink>("BannerWebLink")!;
                open = OpenLink(link);
                Dispatcher.UIThread.RunJobs();
                var prompt = Assert.Single(Prompts());
                Assert.Equal(ConditioningControlPanel.Localization.Loc.GetF("msg_browser_no_default_for", "open the CC Labs web app")
                    + ConditioningControlPanel.Localization.Loc.GetF("msg_browser_link_copied", "https://app.cclabs.app/"),
                    prompt.FindControl<TextBlock>("TxtMessage")!.Text);
                Assert.Contains(PanicSurfaces.All, s => s.Id == "browser-prompt");
                clipboard = w.Clipboard!.TryGetTextAsync();
                PanicSurfaces.StopAll("banner-test", w);
                Dispatcher.UIThread.RunJobs();
                Assert.False(prompt.IsVisible);
            });
            await open!;
            Assert.Equal("https://app.cclabs.app/", await clipboard!);
            Assert.Empty(Prompts());
        }
        finally { SafeLink.Refused = refused; }
    });

    [Fact]
    public Task ASandboxRefusalSkipsTheClipboardAndPrompt() => InShell(async w =>
    {
        Assert.True(ConditioningControlPanel.Services.SandboxNet.Active);
        await w.Clipboard!.SetTextAsync("unchanged");
        await OpenLink(w.Named<SafeLink>("BannerWebLink")!);
        Assert.Empty(Prompts());
        Assert.Equal("unchanged", await w.Clipboard.TryGetTextAsync());
    });

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public Task PendingBrowserFailureStopsOnPanicOrOwnerClose(bool delayClipboard, bool closeOwner) => InShell(async w =>
    {
        var refused = SafeLink.Refused;
        var release = new TaskCompletionSource<bool>();
        Task? pending = null;
        try
        {
            SafeLink.Refused = _ => false;
            var link = w.Named<SafeLink>("BannerWebLink")!;
            var copied = 0;
            var shown = 0;
            using var sub = Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) =>
            {
                if (window is ConditioningControlPanel.Avalonia.Views.Dialogs.MessageDialog) shown++;
            });
            link.LaunchLink = (_, _) => delayClipboard ? Task.FromResult(false) : release.Task;
            link.CopyLink = async (_, _) => { copied++; if (delayClipboard) await release.Task; };
            pending = OpenLink(link);
            Assert.False(pending.IsCompleted);
            Assert.Equal(delayClipboard ? 1 : 0, copied);
            if (closeOwner) w.Close();
            else PanicSurfaces.StopAll("pending-browser-test", w);
            release.SetResult(false);
            Dispatcher.UIThread.RunJobs();
            Assert.True(pending.IsCompleted);
            await pending;
            Assert.Empty(Prompts());
            Assert.Equal(0, shown);
            Assert.False(link.IsVisited);
            Assert.Equal(delayClipboard ? 1 : 0, copied);
        }
        finally
        {
            release.TrySetResult(false);
            SafeLink.CloseBrowserPrompts();
            try { if (pending is not null) await pending; }
            finally { SafeLink.Refused = refused; }
        }
    });

    private static Task OpenLink(SafeLink link) =>
        (Task)typeof(SafeLink).GetMethod("OpenAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(link, new object[] { link.NavigateUri! })!;
    private static System.Collections.Generic.HashSet<Window> Prompts() =>
        (System.Collections.Generic.HashSet<Window>)typeof(SafeLink)
            .GetField("Prompts", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;

    internal static Task InShell(Action<MainShellWindow> action) => InShell(w => { action(w); return Task.CompletedTask; });

    private static Task InShell(Func<MainShellWindow, Task> action) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var motion = CoreSettings.Current.MotionLevel;
        var seen = CoreSettings.Current.SeenFeatureIntros.ToArray();
        CoreSettings.Current.SeenFeatureIntros.Remove(MainShellWindow.WebBannerSeenKey);
        var w = new MainShellWindow();
        try { w.Show(); Dispatcher.UIThread.RunJobs(); await action(w); }
        finally
        {
            w.Close();
            CoreSettings.Current.MotionLevel = motion;
            CoreSettings.Current.SeenFeatureIntros.Clear();
            CoreSettings.Current.SeenFeatureIntros.AddRange(seen);
        }
    });

    internal static void Tick(MainShellWindow w) => typeof(MainShellWindow)
        .GetMethod("BannerRotationTimer_Tick", BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(w, new object?[] { null, EventArgs.Empty });
}
