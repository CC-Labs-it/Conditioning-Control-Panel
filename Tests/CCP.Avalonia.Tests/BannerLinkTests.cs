using Avalonia.Input.Platform;
using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using Xunit;
using SafeLink = ConditioningControlPanel.Avalonia.Controls.SafeHyperlinkButton;

namespace CCP.Avalonia.Tests;

/// <summary>Restack b78 (from #3466): the banner's WPF sizes, the live support link and the web link's
/// copy-and-explain fallback (WPF MainWindow.xaml:1078-1146, BrowserLauncher.OpenUrlOrPrompt).</summary>
public sealed class BannerLinkTests
{
    [Fact]
    public Task BannerUsesWpfDimensions() => InShell(w =>
    {
        var host = w.Named<Border>("HeaderBannerHost")!;
        Assert.Equal(31, host.Height);
        Assert.Equal(744, host.MaxWidth);
        Assert.Equal(31, w.Named<Border>("HeaderBannerGlowHost")!.Height);
        foreach (var name in new[] { "TxtBannerPrimary", "TxtBannerSecondary", "TxtBannerWeb", "TxtBannerPool" })
            Assert.Equal(14, w.Named<TextBlock>(name)!.FontSize);
    });

    [Fact]
    public Task SupportLinkUsesTheGuardedOpener() => InShell(w =>
    {
        var link = Assert.IsType<SafeLink>(w.Named<SafeLink>("BannerSupportLink"));
        Assert.Equal("https://linktr.ee/CodeBambi", link.NavigateUri!.AbsoluteUri);
        var shell = ConditioningControlPanel.Avalonia.Platform.ExternalOpener.Shell;
        string? target = null;
        try
        {
            ConditioningControlPanel.Avalonia.Platform.ExternalOpener.Shell = url => { target = url; return true; };
            // The sandbox must still refuse the click before it reaches the shell.
            typeof(SafeLink).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(link, null);
            Dispatcher.UIThread.RunJobs();
            Assert.Null(target);
        }
        finally { ConditioningControlPanel.Avalonia.Platform.ExternalOpener.Shell = shell; }
    });

    [Fact]
    public Task WebLinkFailureCopiesTheLinkAndExplains() => InShell(async w =>
    {
        var refused = SafeLink.Refused;
        var link = w.Named<SafeLink>("BannerWebLink")!;
        try
        {
            // A browser failure, not a sandbox refusal: only then does WPF's BrowserLauncher fallback apply.
            SafeLink.Refused = _ => false;
            link.LaunchLink = (_, _) => Task.FromResult(false);
            var open = OpenLink(link);
            var prompt = await WaitFor(() => w.OwnedWindows.OfType<MessageDialog>().SingleOrDefault());
            Assert.Equal(Loc.GetF("msg_browser_no_default_for", "open the CC Labs web app")
                + Loc.GetF("msg_browser_link_copied", "https://app.cclabs.app/"),
                prompt.FindControl<TextBlock>("TxtMessage")!.Text);
            Assert.Equal("https://app.cclabs.app/", await w.Clipboard!.TryGetTextAsync());
            prompt.Close();
            await open;
            Assert.False(link.IsVisited);
        }
        finally { SafeLink.Refused = refused; }
    });

    [Fact]
    public Task ASandboxRefusalSkipsTheClipboardAndPrompt() => InShell(async w =>
    {
        Assert.True(ConditioningControlPanel.Services.SandboxNet.Active);
        await w.Clipboard!.SetTextAsync("unchanged");
        await OpenLink(w.Named<SafeLink>("BannerWebLink")!);
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(w.OwnedWindows.OfType<MessageDialog>());
        Assert.Equal("unchanged", await w.Clipboard.TryGetTextAsync());
    });

    [Fact]
    public Task ALinkWithoutAPurposeStaysSilentOnFailure() => InShell(async w =>
    {
        var refused = SafeLink.Refused;
        try
        {
            SafeLink.Refused = _ => false;
            var link = w.Named<SafeLink>("BannerSupportLink")!;
            link.LaunchLink = (_, _) => Task.FromResult(false);
            await OpenLink(link);
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(w.OwnedWindows.OfType<MessageDialog>());
        }
        finally { SafeLink.Refused = refused; }
    });

    private static Task OpenLink(SafeLink link) =>
        (Task)typeof(SafeLink).GetMethod("OpenAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(link, new object[] { link.NavigateUri! })!;

    private static async Task<T> WaitFor<T>(Func<T?> probe) where T : class
    {
        for (var i = 0; i < 200; i++)
        {
            Dispatcher.UIThread.RunJobs();
            if (probe() is { } hit) return hit;
            await Task.Delay(10);
        }
        throw new TimeoutException();
    }

    private static Task InShell(Action<MainShellWindow> action) => InShell(w => { action(w); return Task.CompletedTask; });

    private static Task InShell(Func<MainShellWindow, Task> action) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        LocalizationManager.Instance.SetLanguage("en");
        var seen = CoreSettings.Current.SeenFeatureIntros.ToArray();
        CoreSettings.Current.SeenFeatureIntros.Remove(MainShellWindow.WebBannerSeenKey);
        var w = new MainShellWindow();
        try { w.Show(); Dispatcher.UIThread.RunJobs(); await action(w); }
        finally
        {
            w.Close();
            CoreSettings.Current.SeenFeatureIntros.Clear();
            CoreSettings.Current.SeenFeatureIntros.AddRange(seen);
        }
    });
}
