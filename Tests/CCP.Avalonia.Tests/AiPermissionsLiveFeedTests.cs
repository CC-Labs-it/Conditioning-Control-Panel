using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Models.CommandData;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.AIService;
using ConditioningControlPanel.Services.Commands;
using Mode = ConditioningControlPanel.Views.Controls.Companion.CompanionProviderMode;
using Xunit;

namespace CCP.Avalonia.Tests;

[Collection(RunsAloneCollection.Name)]
public sealed class AiPermissionsLiveFeedTests
{
    [Fact]
    public Task RealCommandUpdatesTheBoundCollectionAndWorkerLinesReachTheUi() => Run(async service =>
    {
        var previousSink = AiCommandService.LiveActionSink;
        var previousFlash = FlashImageCommand.Surface;
        var previousLab = CoreAccount.HasLabAccessProvider;
        Window? window = null;
        try
        {
            CompanionEffects.Seed();
            Assert.NotNull(AiCommandService.LiveActionSink);
            service.Current.AiChatEnabled = true;
            service.Current.CompanionPrompt.AiProvider = AiProviderType.Local;
            service.Current.CompanionPrompt.AllowAiToControlEffects = true;
            service.Current.CompanionPrompt.AllowAiFlash = true;
            CoreAccount.HasLabAccessProvider = () => true;
            var drawer = new EngineRoomDrawer();
            var vm = (EngineRoomVm)drawer.DataContext!;
            var feed = Assert.IsType<ObservableCollection<string>>(vm.LiveActions);
            feed.Clear();
            vm.IsExpanded = true;
            window = new Window { Width = 900, Height = 600, Content = drawer };
            window.Show();
            FlashImageCommand.Surface = (_, _, _) => true;
            new AiCommandService().ExecuteCommand(new AiCommandData {
                Command = AICommandType.flash_image, Data = new FlashImage(3, 4, 100, 100) });
            Assert.Contains("Flash · 3 images for 4s", Assert.Single(feed));
            await Task.Run(() => AiCommandService.LiveActionSink!("⛔ Follow-up flash_image blocked"));
            Dispatcher.UIThread.RunJobs();
            Assert.Same(feed, vm.LiveActions);
            Assert.Equal(2, feed.Count);
            Assert.EndsWith("Follow-up flash_image blocked", feed.Last());
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, drawer.FindControl<ItemsControl>("LiveActionsFeed")!.Items.Count);
            feed.Clear();
        }
        finally
        {
            window?.Close();
            AiCommandService.LiveActionSink = previousSink;
            FlashImageCommand.Surface = previousFlash;
            CoreAccount.HasLabAccessProvider = previousLab;
        }
    });

    [Fact]
    public Task ProviderPicksShowLocalFeedAndClearOldEntriesForCloudAndOff() => Run(_ =>
    {
        var drawer = new EngineRoomDrawer();
        var vm = (EngineRoomVm)drawer.DataContext!;
        vm.Provider = Mode.LocalOllama;
        Assert.True(vm.ShowLiveActions);
        var feed = Assert.IsType<ObservableCollection<string>>(vm.LiveActions);
        feed.Add("test local action");
        vm.Provider = Mode.Custom;
        Assert.True(vm.ShowLiveActions);
        Assert.Single(feed);
        vm.Provider = Mode.Cloud;
        Assert.False(vm.ShowLiveActions);
        Assert.Empty(feed);
        vm.Provider = Mode.LocalOllama;
        feed.Add("second local action");
        vm.Provider = Mode.Off;
        Assert.False(vm.ShowLiveActions);
        Assert.Empty(feed);
        return Task.CompletedTask;
    });

    [Fact]
    public Task TurningMemoryOffAlsoDeletesLegacyLocalHistory() => Run(_ =>
    {
        Assert.Equal(TestUserDataProfile.Root, CorePaths.UserData);
        var history = Path.Combine(CorePaths.UserData, "local_chat_history.json");
        var previous = File.Exists(history) ? File.ReadAllBytes(history) : null;
        var oldAi = App.Ai;
        try
        {
            File.WriteAllText(history, "[]");
            App.Ai = new AiServiceStrategy();
            CoreSettings.Current.CompanionPrompt.ChatMemoryEnabled = true;
            var permissions = new AiPermissionsGrid();
            permissions.FindControl<CheckBox>("ChkChatMemoryEnabled")!.IsChecked = false;
            Assert.False(CoreSettings.Current.CompanionPrompt.ChatMemoryEnabled);
            Assert.False(File.Exists(history));
        }
        finally
        {
            App.Ai = oldAi;
            if (previous is null) File.Delete(history); else File.WriteAllBytes(history, previous);
        }
        return Task.CompletedTask;
    });

    /// <summary>WPF AiPermissionsGrid.xaml.cs:53 / MainWindow.UiUpdates.cs:951: a mid-session upgrade or a
    /// re-show repaints the lock band; Loaded alone left it locked for the rest of the session.</summary>
    [Fact]
    public Task LockBandFollowsTierChangesAndReShowWithoutReload() => Run(_ =>
    {
        var (oldGet, oldSet) = (CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider);
        CoreSecrets.RetrieveProvider = _ => null;
        CoreSecrets.StoreProvider = (_, _) => { };
        Window? window = null;
        try
        {
            Assert.True(ConditioningControlPanel.Avalonia.Platform.AccountSeed.Seed(p => new ProviderSubscription(p, () => new AppSettings())));
            var lab = false;
            CoreAccount.HasLabAccessProvider = () => lab;
            var grid = new AiPermissionsGrid();
            window = new Window { Width = 900, Height = 700, Content = grid };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var band = grid.FindControl<Grid>("EffectsLockBand")!;
            Assert.True(band.IsVisible);

            lab = true;   // signed in / upgraded: the provider says so
            ConditioningControlPanel.Avalonia.Platform.AccountSeed.Patreon!.NotifyEntitlementRaised(PatreonTier.Level2);
            Dispatcher.UIThread.RunJobs();
            Assert.False(band.IsVisible);

            lab = false;  // lapsed while hidden: becoming visible repaints
            grid.IsVisible = false;
            grid.IsVisible = true;
            Dispatcher.UIThread.RunJobs();
            Assert.True(band.IsVisible);
        }
        finally
        {
            window?.Close();
            CoreSecrets.RetrieveProvider = oldGet;
            CoreSecrets.StoreProvider = oldSet;
            ConditioningControlPanel.Avalonia.Platform.AccountSeed.Seed(p => new ProviderSubscription(p, () => new AppSettings()));
            CoreAccount.IsLoggedInProvider = CoreAccount.HasPremiumAccessProvider = CoreAccount.HasLabAccessProvider = CoreAccount.IsWhitelistedProvider = null;
            CoreAccount.DisplayNameProvider = null;
            CoreAccount.ChangeDisplayNameProvider = null;
            CoreAccount.DeleteAccountProvider = null;
            CoreEntitlement.HasPremiumProvider = CoreEntitlement.HasLabProvider = null;
            CoreProgram.HasPremiumProvider = null;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task LockBandCtaOpensSettingsAccountAsWpfDoes() => Run(_ =>
    {
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            shell.ShowTab("permissions");
            Dispatcher.UIThread.RunJobs();
            var permissions = shell.GetVisualDescendants().OfType<AiPermissionsGrid>().Single();
            permissions.FindControl<Button>("BtnEffectsLockCta")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("appsettings", shell.CurrentTab);
            Assert.Equal("account", shell.AppSettingsPage!.CheckedSectionKey);
        }
        finally { foreach (var owned in shell.OwnedWindows.ToArray()) owned.Close(); shell.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task ForgetEverythingKeepsTheFeedOnCancelAndClearsItOnlyOnConfirm() => Run(async _ =>
    {
        var feed = (ObservableCollection<string>)new EngineRoomVm(new EngineRoomDrawer()).LiveActions;
        feed.Clear();
        feed.Add("test local action");
        var permissions = new AiPermissionsGrid();
        var owner = new Window { Width = 800, Height = 600, Content = permissions };
        try
        {
            owner.Show();
            var forget = permissions.FindControl<Button>("BtnClearChatMemory")!;
            forget.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var cancel = await PendingDialog(owner);
            cancel.FindControl<Button>("BtnCancel")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitFor(() => owner.OwnedWindows.Any(x => x.IsVisible) ? null : owner);
            Assert.Single(feed);
            forget.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var confirm = await PendingDialog(owner);
            confirm.FindControl<Button>("BtnOk")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var done = await PendingDialog(owner);
            Assert.Empty(feed);
            done.FindControl<Button>("BtnOk")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
        finally { foreach (var owned in owner.OwnedWindows.ToArray()) owned.Close(); owner.Close(); feed.Clear(); }
    });

    [Fact]
    public Task ActionRowUsesFluentGlyphAndWrappingText() => Run(_ =>
    {
        var row = new AiLiveActionRow { DataContext = "[14:03:05] 💥 Flash · 3 images for 4s" };
        Assert.Single(row.Children.OfType<ConditioningControlPanel.Avalonia.Controls.IconGlyph>());
        var texts = row.Children.OfType<TextBlock>().Select(x => x.Text).ToArray();
        Assert.Equal(new[] { "[14:03:05]", "Flash · 3 images for 4s" }, texts);
        Assert.Equal(global::Avalonia.Media.TextWrapping.Wrap, row.Children.OfType<TextBlock>().Last().TextWrapping);
        return Task.CompletedTask;
    });

    [Fact]
    public Task V2PermissionsStackMemoryBelowEffectsForTheNarrowSheet() => Run(_ =>
    {
        var permissions = new AiPermissionsGrid();
        var effects = permissions.FindControl<StackPanel>("EffectsGateHost")!;
        var columns = (Grid)((Grid)effects.Parent!).Parent!;
        var memory = columns.Children.OfType<Border>().Single();
        Assert.Empty(columns.ColumnDefinitions);
        Assert.Equal(2, columns.RowDefinitions.Count);
        Assert.Equal(0, Grid.GetColumn(memory));
        Assert.Equal(1, Grid.GetRow(memory));
        Assert.Equal(new Thickness(0, 16, 0, 0), memory.Margin);
        Assert.False(permissions.FindControl<Border>("PreviewBetaTag")!.IsVisible);
        Assert.False(permissions.FindControl<Border>("HeaderTierTag")!.IsVisible);
        return Task.CompletedTask;
    });

    private static Task<ConditioningControlPanel.Avalonia.Views.Dialogs.MessageDialog> PendingDialog(Window owner) =>
        WaitFor(() => owner.OwnedWindows.OfType<ConditioningControlPanel.Avalonia.Views.Dialogs.MessageDialog>()
            .FirstOrDefault(x => x.IsVisible));

    /// <summary>P73/P08: bounded by time (10 s), yields instead of sleeping.</summary>
    private static async Task<T> WaitFor<T>(Func<T?> probe) where T : class
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.Elapsed < TimeSpan.FromSeconds(10))
        {
            Dispatcher.UIThread.RunJobs();
            if (probe() is { } hit) return hit;
            await Task.Yield();
        }
        throw new TimeoutException("The permissions dialog state never appeared");
    }

    private static Task Run(Func<SettingsService, Task> body) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var old = CoreSettings.ServiceProvider;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        try { await body(settings); }
        finally { settings.SaveImmediate(); CoreSettings.ServiceProvider = old; Dispatcher.UIThread.RunJobs(); }
    });
}
