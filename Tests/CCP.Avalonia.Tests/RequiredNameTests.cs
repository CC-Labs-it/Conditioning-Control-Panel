using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using ConditioningControlPanel.Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Friends;
using Xunit;

namespace CCP.Avalonia.Tests;

[Collection(RunsAloneCollection.Name)]
public sealed class RequiredNameTests
{
    private static void Setup()
    {
        if (Application.Current == null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().WithInterFont()
                .With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        LocalizationManager.Instance.SetLanguage("en");
    }

    private sealed class Wire : HttpMessageHandler
    {
        public int Checks;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Checks++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent("""{"available":false}""") });
        }
    }

    // WPF MainWindow.DisplayNameAsk.cs:85-90. No window or font is needed for this transaction.
    [Fact]
    public async Task SuccessfulOwnNameNeverChecksAvailabilityButRefusalDoes()
    {
        var old = CoreAccount.ChangeDisplayNameProvider;
        var factory = AccountSeed.NewV2;
        var wire = new Wire();
        try
        {
            AccountSeed.NewV2 = () => new V2AuthService(() => null, wire);
            CoreAccount.ChangeDisplayNameProvider = _ => Task.FromResult((true, (string?)null, (string?)"Own Name"));
            var saved = await MainShellWindow.ChangeRequiredDisplayNameAsync("Own Name");
            Assert.Null(saved.Error);
            Assert.Equal("Own Name", saved.Name);
            Assert.Equal(0, wire.Checks);
            CoreAccount.ChangeDisplayNameProvider = _ => Task.FromResult((false, (string?)"refused", (string?)null));
            var refused = await MainShellWindow.ChangeRequiredDisplayNameAsync("Other");
            Assert.Equal(Loc.GetF("login_name_already_taken", "Other"), refused.Error);
            Assert.Null(refused.Name);
            Assert.Equal(1, wire.Checks);
        }
        finally { CoreAccount.ChangeDisplayNameProvider = old; AccountSeed.NewV2 = factory; }
    }

    // WPF DisplayNameDialog.xaml.cs:44-62,105-166,201-205.
    [Fact]
    public Task RequiredModeValidatesRetriesAndIgnoresAnAnswerAfterClose() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var owner = new Window();
        owner.Show();
        var answer = new TaskCompletionSource<string?>();
        int calls = 0;
        var d = new DisplayNameDialog(null, _ => { calls++; return answer.Task; });
        var shown = d.ShowDialog<bool>(owner);
        var input = d.FindControl<TextBox>("TxtDisplayName")!;
        var confirm = d.FindControl<Button>("BtnConfirm")!;
        void Click() => confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.False(d.FindControl<Button>("BtnCancel")!.IsVisible);
        Assert.True(d.FindControl<Button>("BtnClose")!.IsVisible);
        input.Text = "ab";
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.False(confirm.IsEnabled);
        input.Text = "a@b";
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.False(confirm.IsEnabled);
        Assert.Equal(Loc.Get("name_ask_bad_chars"), d.FindControl<TextBlock>("TxtStatus")!.Text);
        input.Text = "Own Name";
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Click();
        Click();
        Assert.Equal(1, calls);
        Assert.False(input.IsEnabled);
        answer.SetResult("taken");
        await answer.Task;
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(input.IsEnabled);
        Assert.True(confirm.IsEnabled);
        Assert.Equal("taken", d.FindControl<TextBlock>("TxtStatus")!.Text);
        answer = new TaskCompletionSource<string?>();
        Click();
        d.Close(false);
        answer.SetResult(null);
        await answer.Task;
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.False(await shown);
        Assert.Equal("", d.DisplayName);
        owner.Close();
    });

    // WPF MainWindow.DisplayNameAsk.cs:25-30,58-59,78-108.
    [Fact]
    public Task SignInGateAndOwnNameSaveChangeBeforeAnyAvailabilityCheck() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var s = CoreSettings.Current;
        var old = (s.UnifiedId, s.UserDisplayName);
        var change = CoreAccount.ChangeDisplayNameProvider;
        var factory = AccountSeed.NewV2;
        var wire = new Wire();
        var shell = new MainShellWindow();
        shell.Show();
        try
        {
            AccountSeed.NewV2 = () => new V2AuthService(() => s, wire);
            s.UnifiedId = "u1";
            s.UserDisplayName = null;
            await shell.AskDisplayNameAfterSignIn(shell, null, false);
            Assert.Empty(shell.OwnedWindows);
            var ask = shell.AskDisplayNameAfterSignIn(shell, null, true);
            var d = Assert.Single(shell.OwnedWindows.OfType<DisplayNameDialog>());
            CoreAccount.ChangeDisplayNameProvider = _ => Task.FromResult((true, (string?)null, (string?)"Own Name"));
            d.FindControl<TextBox>("TxtDisplayName")!.Text = "Own Name";
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            d.FindControl<Button>("BtnConfirm")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await ask.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal("Own Name", s.UserDisplayName);
            Assert.Equal(0, wire.Checks);
            CoreAccount.ChangeDisplayNameProvider = _ => Task.FromResult((false, (string?)"refused", (string?)null));
            Assert.Equal(Loc.GetF("login_name_already_taken", "Other"), await shell.SaveRequiredDisplayNameAsync("Other"));
            Assert.Equal(1, wire.Checks);
            Assert.Equal("Own Name", s.UserDisplayName);
        }
        finally
        {
            foreach (var d in shell.OwnedWindows.ToArray()) d.Close();
            shell.Close();
            (s.UnifiedId, s.UserDisplayName) = old;
            CoreAccount.ChangeDisplayNameProvider = change;
            AccountSeed.NewV2 = factory;
        }
    });
    private sealed class ProfileWire : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent("""{"user":{"unified_id":"restored-name","display_name":null}}""") });
    }

    private static async Task<DisplayNameDialog> Prompt(MainShellWindow shell)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (!shell.OwnedWindows.OfType<DisplayNameDialog>().Any() && DateTime.UtcNow < until)
            await Task.Delay(10);
        return Assert.Single(shell.OwnedWindows.OfType<DisplayNameDialog>());
    }

    // WPF DisplayNameAsk.cs:34-59: profile arrival, one ask, dismissal, next launch, hold at show.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task RestoredProfileAsksOncePerLaunchAndReoffersAfterAHold(bool defer) =>
        AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var s = CoreSettings.Current;
        var saved = (s.UnifiedId, s.UserDisplayName, s.OfflineMode, AccountSeed.LoadedProfileUid);
        var sync = AccountSeed.Sync;
        var world = FriendsLanding.World;
        MainShellWindow? shell = null;
        Window? blocker = null;
        bool holding = false;
        try
        {
            (s.UnifiedId, s.UserDisplayName, s.OfflineMode) = ("restored-name", null, false);
            AccountSeed.LoadedProfileUid = null;
            AccountSeed.Sync = null;
            FriendsLanding.World = () => new LandingWorld(holding, false, false, true, false, false, false);
            shell = new MainShellWindow();
            shell.Show();
            Assert.Null(shell.NameAskTask); // no ask before a profile has actually loaded
            if (defer) { blocker = new Window(); _ = blocker.ShowDialog<bool>(shell); }
            async Task Load() => Assert.True(await AccountSeed.LoadProfileAsync(new V2AuthService(() => s, new ProfileWire())));
            await Load();
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var queued = Assert.IsAssignableFrom<Task>(shell.NameAskTask);
            if (defer)
            {
                await Task.Delay(850);
                Assert.False(queued.IsCompleted); // another modal owns the screen
                Assert.Empty(shell.OwnedWindows.OfType<DisplayNameDialog>());
                holding = true; // the hold begins after enqueue, before show
                blocker!.Close();
                await queued.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Empty(shell.OwnedWindows.OfType<DisplayNameDialog>());
                holding = false;
                await Load(); // the next profile load may offer again
            }
            var ask = await Prompt(shell);
            ask.Close(false);
            await shell.NameAskTask!.WaitAsync(TimeSpan.FromSeconds(10));
            var done = shell.NameAskTask;
            await Load();
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Same(done, shell.NameAskTask);
            Assert.Empty(shell.OwnedWindows.OfType<DisplayNameDialog>());
            shell.Close();
            shell = new MainShellWindow(); // same restored account, fresh launch latch
            shell.Show();
            (await Prompt(shell)).Close(false);
            await shell.NameAskTask!.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            blocker?.Close();
            if (shell != null) { foreach (var w in shell.OwnedWindows.ToArray()) w.Close(); shell.Close(); }
            (s.UnifiedId, s.UserDisplayName, s.OfflineMode, AccountSeed.LoadedProfileUid) = saved;
            AccountSeed.Sync = sync;
            FriendsLanding.World = world;
        }
    });


}
