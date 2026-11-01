using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion.V2;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.AIService;
using ConditioningControlPanel.Services.Companion.Brain;
using ConditioningControlPanel.Services.Moderation;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class CompanionCapInboxTests
{
    private static Task Run(Func<MainShellWindow, StartupLadder.TestClock, Task> body) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current == null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var oldSettings = CoreSettings.ServiceProvider;
        var oldPremium = CoreAccount.HasPremiumAccessProvider;
        var oldLab = CoreAccount.HasLabAccessProvider;
        var launch = VaultGateDialog.Launch;
        var oldLifetime = Application.Current!.ApplicationLifetime;
        var field = typeof(Application).GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            .Single(f => typeof(IApplicationLifetime).IsAssignableFrom(f.FieldType));
        MainShellWindow? shell = null;
        try
        {
            var service = new SettingsService();
            service.Current.Welcomed = service.Current.HasAcceptedAgeVerification = true;
            service.Current.SeenFeatureIntros.AddRange(FeatureIntros.All.Keys);
            service.Current.AiChatEnabled = true;
            service.Current.UseCompanionBrain = true;
            service.Current.CompanionPrompt.AiProvider = AiProviderType.Local;
            CoreSettings.ServiceProvider = () => service;
            CoreAccount.HasPremiumAccessProvider = () => false;
            CoreAccount.HasLabAccessProvider = () => false;
            StartupLadder.ResetForTests();
            var clock = (StartupLadder.TestClock)StartupLadder.Time;
            var lifetime = new ClassicDesktopStyleApplicationLifetime { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            field.SetValue(Application.Current, lifetime);
            shell = new MainShellWindow();
            lifetime.MainWindow = shell;
            shell.Show();
            await body(shell, clock);
        }
        finally
        {
            try
            {
                InboxNoticeCard.Open?.Close();
                VaultGateDialog.Launch = launch;
                shell?.Close();
                field.SetValue(Application.Current, oldLifetime);
                StartupLadder.ResetForTests();
            }
            finally
            {
                CoreSettings.ServiceProvider = oldSettings;
                CoreAccount.HasPremiumAccessProvider = oldPremium;
                CoreAccount.HasLabAccessProvider = oldLab;
            }
        }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunRestoresProvidersEvenWhenBodyThrows(bool throwFromBody)
    {
        var oldSettings = CoreSettings.ServiceProvider;
        var oldPremium = CoreAccount.HasPremiumAccessProvider;
        var oldLab = CoreAccount.HasLabAccessProvider;
        var baselineSettings = new SettingsService();
        Func<SettingsService?> settings = () => baselineSettings;
        Func<bool> premium = () => true, lab = () => true;
        try
        {
            CoreSettings.ServiceProvider = settings;
            CoreAccount.HasPremiumAccessProvider = premium;
            CoreAccount.HasLabAccessProvider = lab;
            if (throwFromBody)
                await Assert.ThrowsAsync<InvalidOperationException>(() => Run((_, _) => throw new InvalidOperationException()));
            else
                await Run((_, _) => Task.CompletedTask);
            Assert.Same(settings, CoreSettings.ServiceProvider);
            Assert.Same(premium, CoreAccount.HasPremiumAccessProvider);
            Assert.Same(lab, CoreAccount.HasLabAccessProvider);
        }
        finally
        {
            CoreSettings.ServiceProvider = oldSettings;
            CoreAccount.HasPremiumAccessProvider = oldPremium;
            CoreAccount.HasLabAccessProvider = oldLab;
        }
    }

    [Fact]
    public Task DailyLimitFromConversationWaitsForDepartureThenOpensBasicCheckout() => Run(async (shell, clock) =>
    {
        using var dir = new TempDir();
        var ai = new FailureAi(AiFailureKind.DailyLimit);
        var oldAi = AvApp.Ai;
        var oldBrain = AvApp.Brain;
        using var brain = new CompanionBrain(ai, memory: new MemoryStore(Path.Combine(dir.Path, "memory.json")),
            store: new CompanionSessionStore(Path.Combine(dir.Path, "session.json"), Path.Combine(dir.Path, "legacy.json")), preview: () => true);
        AvApp.Ai = ai;
        AvApp.Brain = brain;
        try
        {
            shell.ShowTab("companion");
            Dispatcher.UIThread.RunJobs();
            var vm = shell.GetLogicalDescendants().OfType<ConversationPage>().Single().Vm;
            vm.Refresh();
            vm.Draft = "Hello";
            await vm.SendAsync();
            Assert.Equal(Loc.Get("companion_v2_error_limit"), vm.Notice);
            Assert.Empty(StartupLadder.Inbox.Items);
            clock.Advance(TimeSpan.FromSeconds(20));
            Assert.Empty(StartupLadder.Inbox.Items);
            shell.ShowTab("achievements");
            clock.Advance(TimeSpan.FromSeconds(9));
            Assert.Empty(StartupLadder.Inbox.Items);
            clock.Advance(TimeSpan.FromSeconds(1));
            var row = Assert.Single(StartupLadder.Inbox.Items);
            Assert.Equal("conv:companion_cap", row.Key);
            Assert.Equal("💬", row.Glyph);
            Assert.Equal(Loc.GetF("conv_cap_free", 100, 1000, 2000), row.Title);
            Assert.Equal(Loc.Get("conv_cap_body"), row.Summary);
            Assert.Equal(clock.GetLocalNow().ToString("yyyy-MM-dd"), CoreSettings.Current.InboxStamps["companion_cap"]);
            Assert.True(shell.FindControl<Button>("BtnInbox")!.IsVisible);
            string? url = null;
            VaultGateDialog.Launch = value => { Assert.Null(InboxNoticeCard.Open); url = value; return true; };
            StartupLadder.Inbox.Open(row);
            var card = Assert.IsType<InboxNoticeCard>(InboxNoticeCard.Open);
            var button = card.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "Primary");
            Assert.Equal(Loc.Get("conv_get_basic"), button.Content);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("https://www.patreon.com/checkout/CodeBambi?rid=27604786", url);
            shell.OnCompanionCapHit();
            clock.Advance(TimeSpan.FromSeconds(10));
            Assert.Empty(StartupLadder.Inbox.Items);
            clock.Advance(TimeSpan.FromDays(1));
            shell.OnCompanionCapHit();
            clock.Advance(TimeSpan.FromSeconds(10));
            Assert.Single(StartupLadder.Inbox.Items);
        }
        finally { AvApp.Ai = oldAi; AvApp.Brain = oldBrain; }
    });

    [Fact]
    public Task TierIsRecheckedAtFilingAndPrimeReceivesNoOffer() => Run((shell, clock) =>
    {
        shell.OnCompanionCapHit();
        CoreAccount.HasLabAccessProvider = () => true;
        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Empty(StartupLadder.Inbox.Items);
        Assert.False(CoreSettings.Current.InboxStamps.ContainsKey("companion_cap"));
        shell.OnCompanionCapHit();
        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Empty(StartupLadder.Inbox.Items);
        CoreAccount.HasLabAccessProvider = () => false;
        CoreAccount.HasPremiumAccessProvider = () => true;
        shell.OnCompanionCapHit();
        clock.Advance(TimeSpan.FromSeconds(10));
        var row = Assert.Single(StartupLadder.Inbox.Items);
        Assert.Equal(Loc.GetF("conv_cap_basic", 1000, 2000), row.Title);
        string? url = null;
        VaultGateDialog.Launch = value => { url = value; return true; };
        StartupLadder.Inbox.Open(row);
        var card = Assert.IsType<InboxNoticeCard>(InboxNoticeCard.Open);
        var button = card.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "Primary");
        Assert.Equal(Loc.Get("conv_get_prime"), button.Content);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal("https://www.patreon.com/checkout/CodeBambi?rid=27681505", url);
        return Task.CompletedTask;
    });

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("ccp-cap-").FullName;
        public void Dispose() { try { Directory.Delete(Path, true); } catch (IOException) { } }
    }

    private sealed class FailureAi(AiFailureKind failure) : IAiService
    {
        public bool IsAvailable => true;
        public int DailyRequestsRemaining => 0;
        public Task<AiReplyResult> SendAsync(IReadOnlyList<ChatMessage> messages, AiCallOptions options,
            CancellationToken cancellationToken = default) => Task.FromResult(AiReplyResult.Failed(failure));
        public Task<string> GetBambiReplyAsync(string userInput, bool isUserMessage = false) => throw new NotSupportedException();
        public Task<AiReplyResult> GetBambiReplyExAsync(string userInput, bool isUserMessage = false) => throw new NotSupportedException();
        public Task<string?> GetAwarenessReactionAsync(string detectedName, string category, string serviceName = "", string pageTitle = "", TimeSpan? duration = null) => throw new NotSupportedException();
        public Task<string?> GetStillOnReactionAsync(string displayName, string category, TimeSpan duration) => throw new NotSupportedException();
        public Task<string?> GetKeywordCommentAsync(string keyword, string? promptTemplate = null) => throw new NotSupportedException();
        public Task<string?> GetLockScreenReaction(string sentance, int mistakes, int amount, string? promptTemplate = null) => throw new NotSupportedException();
        public Task<string?> GetVideoDoneReaction(string title, string? promptTemplate = null) => throw new NotSupportedException();
        public void Dispose() { }
    }
}
