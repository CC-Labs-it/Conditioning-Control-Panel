using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class PatronGalleryInboxTests
{
    private static Task Run(Action<MainShellWindow> body) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current == null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var settings = new SettingsService();
        settings.Current.Welcomed = settings.Current.HasAcceptedAgeVerification = true;
        settings.Current.SeenFeatureIntros.AddRange(FeatureIntros.All.Keys);
        CoreSettings.ServiceProvider = () => settings;
        CoreAccount.HasPremiumAccessProvider = () => false;
        StartupLadder.ResetForTests();
        var launch = VaultGateDialog.Launch;
        var oldLifetime = Application.Current!.ApplicationLifetime;
        var lifetime = new global::Avalonia.Controls.ApplicationLifetimes.ClassicDesktopStyleApplicationLifetime
        { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        // The shared platform is already initialized. Replace only its lifetime backing field,
        // then restore it, so the production static caller can find the test shell.
        var lifetimeField = typeof(Application).GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            .Single(f => typeof(global::Avalonia.Controls.ApplicationLifetimes.IApplicationLifetime).IsAssignableFrom(f.FieldType));
        lifetimeField.SetValue(Application.Current, lifetime);
        var shell = new MainShellWindow();
        lifetime.MainWindow = shell;
        try { shell.Show(); body(shell); }
        finally
        {
            InboxNoticeCard.Open?.Close();
            VaultGateDialog.Launch = launch;
            foreach (var popup in WindowList().OfType<AchievementPopup>().ToArray()) popup.Close();
            shell.Close();
            lifetimeField.SetValue(Application.Current, oldLifetime);
            StartupLadder.ResetForTests();
        }
        return Task.CompletedTask;
    });

    private static System.Collections.Generic.IEnumerable<Window> WindowList() =>
        (Application.Current?.ApplicationLifetime as global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.Windows
        ?? Array.Empty<Window>();
    private static void Drain() => Dispatcher.UIThread.RunJobs();
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [Fact]
    public Task UnlockFilesQuietlyAndOpenHasWpfCopyAndCheckout() => Run(shell =>
    {
        string? url = null;
        VaultGateDialog.Launch = value =>
        {
            Assert.Null(InboxNoticeCard.Open); // WPF closes before invoking the action.
            url = value;
            return true;
        };
        AvApp.ShowAchievementPopup(new Achievement { Name = "Test", IsExclusive = false });
        Drain();
        var row = Assert.Single(StartupLadder.Inbox.Items);
        Assert.Equal("conv:patron_gallery", row.Key);
        Assert.Equal("🏅", row.Glyph);
        Assert.Equal(Loc.GetF("conv_gallery_title", Achievement.All.Values.Count(a => a.IsExclusive && !a.IsHidden)), row.Title);
        Assert.Equal(Loc.Get("conv_gallery_body"), row.Summary);
        Assert.Null(InboxNoticeCard.Open);
        Assert.True(shell.FindControl<Button>("BtnInbox")!.IsVisible);
        StartupLadder.Inbox.Open(row);
        var card = Assert.IsType<InboxNoticeCard>(InboxNoticeCard.Open);
        Assert.Equal(400, card.Width);
        Assert.Same(shell, card.Owner);
        var primary = card.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "Primary");
        Assert.Equal(Loc.Get("conv_get_basic"), primary.Content);
        Click(primary);
        Assert.Equal("https://www.patreon.com/checkout/CodeBambi?rid=27604786", url);
    });

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public Task PaidOrExclusiveUnlockDoesNotFile(bool paid, bool exclusive) => Run(shell =>
    {
        CoreAccount.HasPremiumAccessProvider = () => paid;
        shell.PostPatronGalleryRow(new Achievement { IsExclusive = exclusive });
        Drain();
        Assert.Empty(StartupLadder.Inbox.Items);
        Assert.False(CoreSettings.Current.InboxStamps.ContainsKey("patron_gallery"));
    });

    [Fact]
    public Task DismissAndRepeatedUnlocksKeepLocalDailyStamp() => Run(shell =>
    {
        var clock = (StartupLadder.TestClock)StartupLadder.Time;
        shell.PostPatronGalleryRow(new Achievement());
        shell.PostPatronGalleryRow(new Achievement());
        Drain();
        StartupLadder.Inbox.Dismiss(Assert.Single(StartupLadder.Inbox.Items));
        shell.PostPatronGalleryRow(new Achievement());
        Drain();
        Assert.Empty(StartupLadder.Inbox.Items);
        Assert.Equal(clock.GetLocalNow().ToString("yyyy-MM-dd"), CoreSettings.Current.InboxStamps["patron_gallery"]);
        clock.Advance(TimeSpan.FromDays(1));
        shell.PostPatronGalleryRow(new Achievement());
        Drain();
        Assert.Single(StartupLadder.Inbox.Items);
    });

    [Theory]
    [InlineData("replace")]
    [InlineData("quiet")]
    [InlineData("escape")]
    public Task NoticeReplacementQuietAndEscapeDoNotLaunch(string action) => Run(shell =>
    {
        var actions = 0;
        InboxNoticeCard.Present(shell, "Heading", "Body", "Action", () => actions++);
        var first = InboxNoticeCard.Open!;
        if (action == "replace")
        {
            InboxNoticeCard.Present(shell, "Heading", "Body", "Action", () => actions++);
            Assert.False(first.IsVisible);
            Assert.NotSame(first, InboxNoticeCard.Open);
        }
        else
        {
            if (action == "quiet")
                Click(first.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "Quiet"));
            else first.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
            Assert.Null(InboxNoticeCard.Open);
        }
        Assert.Equal(0, actions);
    });
}
