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
using ConditioningControlPanel.Avalonia.Controls;
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

/// <summary>WPF MainWindow.Inbox.cs:421-541 (restack b159, from #3792 and #3913): the companion cap
/// offer and the patron gallery note are filed quietly, once a local day, and open the tier checkout.</summary>
public sealed class ConversionInboxRowsTests
{
    private const string BasicUrl = "https://www.patreon.com/checkout/CodeBambi?rid=27604786";
    private const string PrimeUrl = "https://www.patreon.com/checkout/CodeBambi?rid=27681505";

    private static Task Run(Func<MainShellWindow, StartupLadder.TestClock, List<string>, Task> body) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current == null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var oldSettings = CoreSettings.ServiceProvider;
        var oldPremium = CoreAccount.HasPremiumAccessProvider;
        var oldLab = CoreAccount.HasLabAccessProvider;
        var oldOpener = MainShellWindow.PlanOpener;
        var oldLifetime = Application.Current!.ApplicationLifetime;
        // The shared platform is already initialized: swap only its lifetime backing field (restored below),
        // so MainShellWindow.Current finds this test's shell from the production static callers.
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
            var opened = new List<string>();
            MainShellWindow.PlanOpener = url => { opened.Add(url); return true; };
            StartupLadder.ResetForTests();
            var clock = (StartupLadder.TestClock)StartupLadder.Time;
            var lifetime = new ClassicDesktopStyleApplicationLifetime { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            field.SetValue(Application.Current, lifetime);
            shell = new MainShellWindow();
            lifetime.MainWindow = shell;
            shell.Show();
            await body(shell, clock, opened);
        }
        finally
        {
            try
            {
                InboxNoticeCard.Open?.Close();
                foreach (var popup in lifetimeWindows().OfType<AchievementPopup>().ToArray()) popup.Close();
                shell?.Close();
                field.SetValue(Application.Current, oldLifetime);
                StartupLadder.ResetForTests();
            }
            finally
            {
                CoreSettings.ServiceProvider = oldSettings;
                CoreAccount.HasPremiumAccessProvider = oldPremium;
                CoreAccount.HasLabAccessProvider = oldLab;
                MainShellWindow.PlanOpener = oldOpener;
            }
        }

        IEnumerable<Window> lifetimeWindows() =>
            (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Windows ?? Array.Empty<Window>();
    });

    private static void Drain() => Dispatcher.UIThread.RunJobs();

    private static string ClickPrimary()
    {
        var card = Assert.IsType<InboxNoticeCard>(InboxNoticeCard.Open);
        var button = card.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "NoticePrimary");
        var label = ((TextBlock)button.Content!).Text!;
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        return label;
    }

    private static FluentIcons.Common.Symbol IconOf(string glyph)
    {
        Assert.True(IconMap.TryGet(glyph, out var icon));
        return icon.Kind;
    }

    [Fact]
    public Task FreeUnlockFilesGalleryRowQuietlyAndOpensBasicCheckout() => Run((shell, _, opened) =>
    {
        AvApp.ShowAchievementPopup(new Achievement { Name = "Test", IsExclusive = false });
        Drain();
        var row = Assert.Single(StartupLadder.Inbox.Items);
        Assert.Equal("conv:patron_gallery", row.Key);
        Assert.Equal(FluentIcons.Common.Symbol.Trophy, IconOf(row.Glyph));
        Assert.Equal(Loc.GetF("conv_gallery_title", Achievement.All.Values.Count(a => a.IsExclusive && !a.IsHidden)), row.Title);
        Assert.Equal(Loc.Get("conv_gallery_body"), row.Summary);
        Assert.Null(InboxNoticeCard.Open);   // filed, never presented
        StartupLadder.Inbox.Open(row);
        Assert.Same(shell, InboxNoticeCard.Open!.Owner);
        Assert.Equal(Loc.Get("conv_get_basic"), ClickPrimary());
        Assert.Equal(new[] { BasicUrl }, opened);
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public Task PaidOrExclusiveUnlockDoesNotFile(bool paid, bool exclusive) => Run((shell, _, _) =>
    {
        CoreAccount.HasPremiumAccessProvider = () => paid;
        shell.PostPatronGalleryRow(new Achievement { IsExclusive = exclusive });
        Drain();
        Assert.Empty(StartupLadder.Inbox.Items);
        Assert.False(CoreSettings.Current.InboxStamps.ContainsKey("patron_gallery"));
        return Task.CompletedTask;
    });

    [Fact]
    public Task GalleryRowKeepsItsLocalDailyStampAfterDismiss() => Run((shell, clock, _) =>
    {
        shell.PostPatronGalleryRow(new Achievement());
        shell.PostPatronGalleryRow(new Achievement());
        Drain();
        StartupLadder.Inbox.Dismiss(Assert.Single(StartupLadder.Inbox.Items));
        shell.PostPatronGalleryRow(new Achievement());
        Drain();
        Assert.Empty(StartupLadder.Inbox.Items);
        Assert.Equal(clock.GetLocalNow().ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            CoreSettings.Current.InboxStamps["patron_gallery"]);
        clock.Advance(TimeSpan.FromDays(1));
        shell.PostPatronGalleryRow(new Achievement());
        Drain();
        Assert.Single(StartupLadder.Inbox.Items);
        return Task.CompletedTask;
    });

    [Fact]
    public Task DailyLimitWaitsForTheConversationToGoThenOffersBasic() => Run(async (shell, clock, opened) =>
    {
        var dir = Directory.CreateTempSubdirectory("ccp-cap-").FullName;
        var ai = new FailureAi(AiFailureKind.DailyLimit);
        var oldAi = AvApp.Ai;
        var oldBrain = AvApp.Brain;
        var brain = new CompanionBrain(ai, memory: new MemoryStore(Path.Combine(dir, "memory.json")),
            store: new CompanionSessionStore(Path.Combine(dir, "session.json"), Path.Combine(dir, "legacy.json")), preview: () => true);
        AvApp.Ai = ai;
        AvApp.Brain = brain;
        try
        {
            shell.ShowTab("companion");
            Drain();
            var vm = shell.GetLogicalDescendants().OfType<ConversationPage>().Single().Vm;
            vm.Refresh();
            vm.Draft = "Hello";
            await vm.SendAsync();
            Assert.Equal(Loc.Get("companion_v2_error_limit"), vm.Notice);
            clock.Advance(TimeSpan.FromSeconds(20));
            Assert.Empty(StartupLadder.Inbox.Items);   // still on the conversation
            shell.ShowTab("achievements");
            clock.Advance(TimeSpan.FromSeconds(9));
            Assert.Empty(StartupLadder.Inbox.Items);
            clock.Advance(TimeSpan.FromSeconds(1));
            var row = Assert.Single(StartupLadder.Inbox.Items);
            Assert.Equal("conv:companion_cap", row.Key);
            Assert.Equal(FluentIcons.Common.Symbol.Chat, IconOf(row.Glyph));
            Assert.Equal(Loc.GetF("conv_cap_free", 100, 1000, 2000), row.Title);
            Assert.Equal(Loc.Get("conv_cap_body"), row.Summary);
            Assert.Null(InboxNoticeCard.Open);
            StartupLadder.Inbox.Open(row);
            Assert.Equal(Loc.Get("conv_get_basic"), ClickPrimary());
            Assert.Equal(new[] { BasicUrl }, opened);
            // Once a local day.
            shell.OnCompanionCapHit();
            clock.Advance(TimeSpan.FromSeconds(10));
            Assert.Empty(StartupLadder.Inbox.Items);
            clock.Advance(TimeSpan.FromDays(1));
            shell.OnCompanionCapHit();
            clock.Advance(TimeSpan.FromSeconds(10));
            Assert.Single(StartupLadder.Inbox.Items);
        }
        finally
        {
            AvApp.Ai = oldAi;
            AvApp.Brain = oldBrain;
            brain.Dispose();
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    });

    [Fact]
    public Task TierIsRecheckedAtFilingAndPrimeGetsNoOffer() => Run((shell, clock, opened) =>
    {
        shell.ShowTab("achievements");
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
        StartupLadder.Inbox.Open(row);
        Assert.Equal(Loc.Get("conv_get_prime"), ClickPrimary());
        Assert.Equal(new[] { PrimeUrl }, opened);
        return Task.CompletedTask;
    });

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
