using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class RemoteMediaOfferModalTests
{
    // Drive the updater's existing ownership flag without a window, network request or font dependency.
    private static readonly FieldInfo Busy = typeof(AppUpdater).GetField("_offering", BindingFlags.Static | BindingFlags.NonPublic)!;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task A_modal_parks_the_offer_without_spending_the_launch_claim(bool update) => WithOffer(opened =>
    {
        Busy.SetValue(null, update);
        MainShellWindow.IsStartupDialogShowing = !update;
        RemoteMediaOffer.Offer("video");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, opened());
        Assert.Empty(StartupLadder.Inbox.Items);
        RemoteMediaOffer.Offer("another-empty-surface");
        RemoteMediaOffer.FlushPending(); // still blocked: keep the request owed
        Assert.Equal(0, opened());
        Busy.SetValue(null, false);
        MainShellWindow.IsStartupDialogShowing = false;
        RemoteMediaOffer.FlushPending();
        Assert.Equal(1, opened());
        RemoteMediaOffer.FlushPending();
        StartupLadder.ResetForTests();
        RemoteMediaOffer.Offer("video");
        Assert.Equal(1, opened()); // one claim across every surface this launch
    });

    [Theory]
    [InlineData("online")]
    [InlineData("mixed")]
    public Task A_parked_offer_rechecks_the_source_before_flushing(string source) => WithOffer(opened =>
    {
        MainShellWindow.IsStartupDialogShowing = true;
        RemoteMediaOffer.Offer("video");
        CoreSettings.Current.MediaSource = source;
        MainShellWindow.IsStartupDialogShowing = false;
        RemoteMediaOffer.FlushPending();
        Assert.Equal(0, opened());
        CoreSettings.Current.MediaSource = "local";
        RemoteMediaOffer.FlushPending();
        Assert.Equal(0, opened()); // dropped, not a stale request for the next launch
        RemoteMediaOffer.Offer("video");
        Assert.Equal(1, opened()); // the dropped request never spent the claim
    });

    [Fact]
    public Task An_Inbox_row_opened_during_an_update_is_parked_and_retried() => WithOffer(opened =>
    {
        MainShellWindow.IsStartupDialogShowing = true;
        RemoteMediaOffer.Offer("video");
        StartupLadder.BeginFirstLaunchQuiet(TimeSpan.FromMinutes(10));
        MainShellWindow.IsStartupDialogShowing = false;
        RemoteMediaOffer.FlushPending();
        var row = Assert.Single(StartupLadder.Inbox.Items);
        Assert.Equal(0, opened());
        Busy.SetValue(null, true);
        StartupLadder.Inbox.Open(row);
        Assert.Equal(0, opened());
        Busy.SetValue(null, false);
        RemoteMediaOffer.FlushPending();
        row = Assert.Single(StartupLadder.Inbox.Items);
        StartupLadder.Inbox.Open(row);
        Assert.Equal(1, opened());
    });

    [Fact]
    public Task A_startup_park_waits_for_the_flush_and_then_for_the_modal() => WithOffer(opened =>
    {
        // WPF App.xaml.cs:229 records only; MainWindow.xaml.cs:3852 surfaces it.
        Task.Run(() => RemoteMediaOffer.Park("custom-assets-path-missing")).GetAwaiter().GetResult();
        RemoteMediaOffer.Park("later");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, opened());
        MainShellWindow.IsStartupDialogShowing = true;
        RemoteMediaOffer.FlushPending();
        Assert.Equal(0, opened()); // behind the wizard: re-parked, claim unspent
        MainShellWindow.IsStartupDialogShowing = false;
        RemoteMediaOffer.FlushPending();
        Assert.Equal(1, opened());
        RemoteMediaOffer.FlushPending();
        StartupLadder.ResetForTests();
        RemoteMediaOffer.Offer("flashes");
        Assert.Equal(1, opened());
    });

    [Fact]
    public Task A_startup_park_and_a_modal_offer_share_one_slot() => WithOffer(opened =>
    {
        // WPF has one pending slot (App.xaml.cs:890, :921, :229): a flush that drops the request leaves nothing stale.
        RemoteMediaOffer.Park("custom-assets-path-missing");
        MainShellWindow.IsStartupDialogShowing = true;
        RemoteMediaOffer.Offer("video");
        MainShellWindow.IsStartupDialogShowing = false;
        CoreSettings.Current.MediaSource = "online";
        RemoteMediaOffer.FlushPending();
        CoreSettings.Current.MediaSource = "local";
        RemoteMediaOffer.FlushPending();
        Assert.Equal(0, opened());
        RemoteMediaOffer.Offer("flashes");
        Assert.Equal(1, opened());
    });

    [Fact]
    public Task Every_empty_flash_burst_reaches_the_offer() => WithOffer(opened =>
    {
        // WPF FlashService.cs:971 offers outside the warn-once flag, so a later empty burst still offers.
        var flags = typeof(FlashOverlay).GetFields(BindingFlags.Static | BindingFlags.NonPublic)
            .Where(f => f.Name is "_busy" or "_closed").ToArray();
        var oldFlags = flags.Select(f => f.GetValue(null)).ToArray();
        var old = (FlashOverlay.OverlaysAvailable, ScreenList.QueryOverride, CorePaths.EffectiveAssetsProvider, ContentPackStore.Current);
        var empty = Directory.CreateTempSubdirectory("ccp-remote-media-").FullName;
        try
        {
            foreach (var f in flags) f.SetValue(null, false);
            var screen = (Screen)Activator.CreateInstance(typeof(PlatformScreen), new object[] { new PlatformHandle(new IntPtr(1), "headless-test") })!;
            typeof(Screen).GetProperty(nameof(Screen.Bounds))!.SetValue(screen, new PixelRect(0, 0, 1920, 1080));
            typeof(Screen).GetProperty(nameof(Screen.IsPrimary))!.SetValue(screen, true);
            FlashOverlay.OverlaysAvailable = () => true;
            ScreenList.QueryOverride = _ => new[] { screen };
            CorePaths.EffectiveAssetsProvider = () => empty;
            ContentPackStore.Current = null;
            void Burst()
            {
                FlashOverlay.TriggerOnce(new Border());
                for (var i = 0; i < 500 && FlashOverlay.IsBusy; i++) { Thread.Sleep(10); Dispatcher.UIThread.RunJobs(); }
                Assert.False(FlashOverlay.IsBusy);
            }
            CoreSettings.Current.MediaSource = "online";
            Burst();
            Assert.Equal(0, opened());
            CoreSettings.Current.MediaSource = "local";
            Burst();
            Assert.Equal(1, opened());
        }
        finally
        {
            for (var i = 0; i < flags.Length; i++) flags[i].SetValue(null, oldFlags[i]);
            (FlashOverlay.OverlaysAvailable, ScreenList.QueryOverride, CorePaths.EffectiveAssetsProvider, ContentPackStore.Current) = old;
            Directory.Delete(empty, true);
        }
    });

    [Fact]
    public Task A_missing_assets_folder_parks_once_and_the_opened_shell_flushes_it() => WithOffer(opened =>
    {
        // WPF App.xaml.cs:229 only records; MainWindow.xaml.cs:3852 surfaces it on the shell.
        if (Application.Current == null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var settingsProp = typeof(App).GetProperty(nameof(App.Settings))!;
        var old = (App: settingsProp.GetValue(null), CoreSettings.ServiceProvider, RemoteMediaOffer.ShowCard);
        var service = new SettingsService { SaveDebounceDueTimeMilliseconds = Timeout.Infinite };
        MainShellWindow? shell = null;
        try
        {
            var s = service.Current;
            s.CustomAssetsPath = Path.Combine(Path.GetTempPath(), "ccp-missing-" + Guid.NewGuid().ToString("N"));
            s.MediaSource = "local";
            s.Welcomed = s.HasAcceptedAgeVerification = true;
            s.LastSeenVersion = CoreReleaseContent.AppVersion;
            foreach (var key in FeatureIntros.All.Keys) if (!s.SeenFeatureIntros.Contains(key)) s.SeenFeatureIntros.Add(key);
            settingsProp.SetValue(null, service);
            CoreSettings.ServiceProvider = () => service;
            Window? owner = null;
            RemoteMediaOffer.ShowCard = w => { owner = w; old.ShowCard(w); };

            var app = (App)RuntimeHelpers.GetUninitializedObject(typeof(App));
            var resolve = typeof(App).GetMethod("ResolveEffectiveAssetsPath", BindingFlags.Instance | BindingFlags.NonPublic)!;
            Assert.Equal(app.DefaultAssetsRoot, resolve.Invoke(app, null));
            resolve.Invoke(app, null);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, opened()); // the getter only records

            shell = new MainShellWindow();
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, opened());
            Assert.Same(shell, owner);
            RemoteMediaOffer.FlushPending();
            Assert.Equal(1, opened());
        }
        finally
        {
            if (shell != null) { foreach (var w in shell.OwnedWindows.ToArray()) w.Close(); shell.Close(); }
            Dispatcher.UIThread.RunJobs();
            service.SealForReset();
            settingsProp.SetValue(null, old.App);
            CoreSettings.ServiceProvider = old.ServiceProvider;
        }
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task A_parked_offer_waits_for_the_age_gate(bool accept) => WithOffer(opened =>
    {
        var settingsProp = typeof(App).GetProperty(nameof(App.Settings))!;
        var old = (App: settingsProp.GetValue(null), CoreSettings.ServiceProvider);
        var service = new SettingsService { SaveDebounceDueTimeMilliseconds = Timeout.Infinite };
        MainShellWindow? shell = null;
        try
        {
            var s = service.Current;
            s.Welcomed = true;
            s.HasAcceptedAgeVerification = false;
            s.MediaSource = "local";
            s.LastSeenVersion = CoreReleaseContent.AppVersion;
            foreach (var key in FeatureIntros.All.Keys) if (!s.SeenFeatureIntros.Contains(key)) s.SeenFeatureIntros.Add(key);
            settingsProp.SetValue(null, service);
            CoreSettings.ServiceProvider = () => service;
            RemoteMediaOffer.Park("custom-assets-path-missing");
            shell = new MainShellWindow();
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            var dialog = Assert.Single(shell.OwnedWindows.OfType<ConditioningControlPanel.Avalonia.Views.Dialogs.MessageDialog>());
            Assert.True(dialog.IsVisible);
            Assert.Equal(0, opened());
            RemoteMediaOffer.Offer("flashes", shell);
            Assert.Equal(0, opened());
            // Other startup cards can occupy the passive settle window; isolate this offer's release.
            StartupLadder.ResetForTests();

            dialog.FindControl<Button>(accept ? "BtnOk" : "BtnCancel")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(accept, s.HasAcceptedAgeVerification);
            Assert.Equal(accept ? 1 : 0, opened());
        }
        finally
        {
            if (shell != null) { foreach (var w in shell.OwnedWindows.ToArray()) w.Close(); shell.Close(); }
            Dispatcher.UIThread.RunJobs();
            service.SealForReset();
            settingsProp.SetValue(null, old.App);
            CoreSettings.ServiceProvider = old.ServiceProvider;
        }
    });

    [Fact]
    public Task A_background_offer_is_marshaled_before_reading_modal_state() => WithOffer(opened =>
    {
        Task.Run(() => RemoteMediaOffer.Offer("video")).GetAwaiter().GetResult();
        Assert.Equal(0, opened());
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, opened());
    });

    [Fact]
    public Task An_Inbox_offer_does_not_open_after_the_source_changes() => WithOffer(opened =>
    {
        StartupLadder.BeginFirstLaunchQuiet(TimeSpan.FromMinutes(10));
        RemoteMediaOffer.Offer("video");
        var row = Assert.Single(StartupLadder.Inbox.Items);
        CoreSettings.Current.MediaSource = "online";
        StartupLadder.Inbox.Open(row);
        Assert.Equal(0, opened());
    });

    private static Task WithOffer(Action<Func<int>> body) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var oldSource = CoreSettings.Current.MediaSource;
        var oldShow = RemoteMediaOffer.ShowCard;
        var oldBusy = AppUpdater.IsUpdateDialogActive;
        var oldStartup = MainShellWindow.IsStartupDialogShowing;
        StartupLadder.ResetForTests();
        RemoteMediaOffer.ResetForTests();
        var opened = 0;
        RemoteMediaOffer.ShowCard = _ => opened++;
        CoreSettings.Current.MediaSource = "local";
        Busy.SetValue(null, false);
        MainShellWindow.IsStartupDialogShowing = false;
        try { body(() => opened); }
        finally
        {
            Busy.SetValue(null, oldBusy);
            MainShellWindow.IsStartupDialogShowing = oldStartup;
            CoreSettings.Current.MediaSource = oldSource;
            RemoteMediaOffer.ShowCard = oldShow;
            RemoteMediaOffer.ResetForTests();
            StartupLadder.ResetForTests();
        }
        return Task.CompletedTask;
    });
}
