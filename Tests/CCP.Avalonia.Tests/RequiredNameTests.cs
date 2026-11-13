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
using Avalonia.Automation;
using IconGlyph = ConditioningControlPanel.Avalonia.Controls.IconGlyph;
using IconKind = FluentIcons.Common.Symbol;
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
            // Only the availability probe (V2AuthService.IsDisplayNameAvailableAsync) counts. AccountSeed.NewV2
            // is process-wide, so while this test awaits the dialog another class's V2 call (profile recovery,
            // sync) can land on this wire too; counting those read as a phantom check on CI.
            if (request.RequestUri?.AbsolutePath.EndsWith("/v2/auth/check-name", StringComparison.Ordinal) == true)
                Interlocked.Increment(ref Checks);
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
    [Theory]
    [InlineData(false), InlineData(true)]
    public Task RequiredModeValidatesRetriesAndIgnoresAnAnswerAfterClose(bool throws) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var owner = new Window();
        owner.Show();
        var answer = new TaskCompletionSource<string?>();
        int calls = 0;
        var d = new DisplayNameDialog(null, _ => { calls++; return answer.Task; });
        var shown = d.ShowDialog<bool>(owner);
        var copy = (d.FindControl<TextBlock>("TxtTitle")!.Text, d.FindControl<TextBlock>("TxtPrompt")!.Text);
        LocalizationManager.Instance.SetLanguage("de");
        Assert.Equal(copy, (d.FindControl<TextBlock>("TxtTitle")!.Text, d.FindControl<TextBlock>("TxtPrompt")!.Text));
        LocalizationManager.Instance.SetLanguage("en");
        var input = d.FindControl<TextBox>("TxtDisplayName")!;
        var confirm = d.FindControl<Button>("BtnConfirm")!;
        var close = d.FindControl<Button>("BtnClose")!;
        void Click() => confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.False(d.FindControl<Button>("BtnCancel")!.IsVisible);
        Assert.True(close.IsVisible);
        Assert.Equal(IconKind.Dismiss, Assert.IsType<IconGlyph>(close.Content).Kind);
        Assert.Equal(Loc.Get("name_ask_close_tip"), AutomationProperties.GetName(close));
        Assert.Equal(Loc.Get("name_ask_close_tip"), Assert.IsType<string>(ToolTip.GetTip(close)));
        ToolTip.SetTip(close, "temporary tip"); // the explicit name must beat the app's tooltip fallback
        Assert.Equal(Loc.Get("name_ask_close_tip"), AutomationProperties.GetName(close));
        ToolTip.SetTip(close, Loc.Get("name_ask_close_tip"));
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
        if (throws) answer.SetException(new InvalidOperationException("save failed")); else answer.SetResult("taken");
        if (!throws) await answer.Task;
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(input.IsEnabled);
        Assert.True(confirm.IsEnabled);
        Assert.Equal(throws ? Loc.Get("msg_failed_to_change_display_name") : "taken", d.FindControl<TextBlock>("TxtStatus")!.Text);
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
    [Theory]
    [InlineData(null), InlineData(""), InlineData(" ")]
    public Task SignInGateAndOwnNameSaveChangeBeforeAnyAvailabilityCheck(string? missingName) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var s = CoreSettings.Current;
        var old = (s.UnifiedId, s.UserDisplayName);
        var change = CoreAccount.ChangeDisplayNameProvider;
        var factory = AccountSeed.NewV2;
        var wire = new Wire();
        var providerSlot = typeof(AccountSeed).GetProperty(nameof(AccountSeed.Patreon), System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var priorProvider = AccountSeed.Patreon;
        var shell = new MainShellWindow();
        shell.Show();
        try
        {
            AccountSeed.NewV2 = () => new V2AuthService(() => s, wire);
            s.UnifiedId = "u1";
            s.UserDisplayName = missingName;
            var card = shell.ProfilePage!;
            s.UnifiedId = null;
            card.DisplayOwnProfile();
            Assert.Equal("You", card.FindControl<TextBlock>("TxtProfileViewerName")!.Text);
            s.UnifiedId = "u1";
            card.DisplayOwnProfile();
            Assert.Equal(Loc.Get("account_name_not_set"), card.FindControl<TextBlock>("TxtProfileViewerName")!.Text);
            providerSlot.SetValue(null, new ProviderSubscription("patreon", () => new()) { DisplayName = "Patreon name" });
            card.DisplayOwnProfile();
            Assert.Equal("Patreon name", card.FindControl<TextBlock>("TxtProfileViewerName")!.Text);
            providerSlot.SetValue(null, priorProvider);
            card.DisplayOwnProfile();
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
            Assert.Equal("Own Name", card.FindControl<TextBlock>("TxtProfileViewerName")!.Text);
            Assert.Equal(0, wire.Checks);
            CoreAccount.ChangeDisplayNameProvider = _ => Task.FromResult((false, (string?)"refused", (string?)null));
            Assert.Equal(Loc.GetF("login_name_already_taken", "Other"), await shell.SaveRequiredDisplayNameAsync("Other"));
            Assert.Equal(1, wire.Checks);
            Assert.Equal("Own Name", s.UserDisplayName);
            Assert.Equal("Own Name", card.FindControl<TextBlock>("TxtProfileViewerName")!.Text);
        }
        finally
        {
            foreach (var d in shell.OwnedWindows.ToArray()) d.Close();
            shell.Close();
            (s.UnifiedId, s.UserDisplayName) = old;
            providerSlot.SetValue(null, priorProvider);
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
    [InlineData(false, false), InlineData(true, false), InlineData(false, true)]
    public Task RestoredProfileAsksOncePerLaunchAndReoffersAfterAHold(bool defer, bool modeless) =>
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
            // WPF StartupPresenter.cs:444-468 gates modal occupancy, not modeless tools.
            if (defer || modeless)
            {
                blocker = new Window();
                if (defer) _ = blocker.ShowDialog<bool>(shell);
                else blocker.Show(shell);
                Assert.Equal(defer, blocker.IsDialog);
            }
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
            if (modeless) Assert.True(blocker!.IsVisible); // the prompt did not wait for the tool to close
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
