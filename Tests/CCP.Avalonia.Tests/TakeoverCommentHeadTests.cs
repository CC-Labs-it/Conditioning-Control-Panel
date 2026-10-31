using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.AIService;
using ConditioningControlPanel.Services.Autonomy;
using ConditioningControlPanel.Services.Companion.Brain;
using ConditioningControlPanel.Services.Moderation;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;
public class TakeoverCommentHeadTests
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public Task HeadRoutesAndDisplaysOnlyUsableDistinctReplies(bool useBrain, bool panic) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        using var state = new TestSettingsScope(); var settings = state.Settings;
        var s = settings.Current;
        s.AiChatEnabled = s.AvatarEnabled = s.AvatarMuted = true;
        s.AutonomyConsentGiven = true;
        s.UseCompanionBrain = useBrain; s.AutonomyResumeOnStartup = false;
        CoreEntitlement.HasPremiumProvider = () => true;
        var ai = new ControlledAi(); AvApp.Ai = ai;
        using var memory = new MemoryStore(Path.Combine(TestUserDataProfile.Root, "takeover-memory.json"));
        using var brain = new CompanionBrain(ai, memory: memory, preview: () => false,
            store: new CompanionSessionStore(Path.Combine(TestUserDataProfile.Root, "takeover-session.json"), "unused"));
        AvApp.Brain = brain;
        var shell = new MainShellWindow();
        try
        {
            shell.Show(); s.AutonomyModeEnabled = true; Assert.True(shell.Autonomy.Start(withTimer: false));
            var tube = Assert.IsType<ConditioningControlPanel.Avalonia.Views.AvatarTube.AvatarTubeWindow>(shell.Tube);
            var text = tube.FindControl<TextBlock>("TxtSpeech")!;
            async Task Reply(AiReplyResult result)
            {
                ai.Result = result; await shell.MakeAutonomyCommentAsync(); Dispatcher.UIThread.RunJobs();
            }
            await Reply(new("first line", true, null));
            Assert.Equal("first line", text.Text);
            Assert.True(tube.FindControl<Border>("AiBadge")!.IsVisible);
            if (useBrain)
            {
                Assert.Equal(0, ai.LegacyCalls);
                Assert.False(ai.Options!.Interactive); Assert.Equal(AiPurpose.Reaction, ai.Options.Purpose);
                Assert.Contains(ai.Messages!, m => m.Content.Contains(FrameFormatter.TakeoverCommentEvent()));
                Assert.Equal(new[] { TurnKind.AmbientEvent, TurnKind.AmbientReply }, brain.Session.Turns.Select(t => t.Kind));
            }
            else { Assert.False(ai.LegacyInteractive); Assert.Equal(1, ai.LegacyCalls); Assert.Equal(TakeoverComment.Prompt, ai.Prompt); Assert.Empty(brain.Session.Turns); }
            await Reply(new(" FIRST LINE ", true, null)); Assert.Equal("first line", text.Text);
            await Reply(new("blocked", true, new(null, ModerationSource.Output))); Assert.Equal("first line", text.Text);
            Assert.False(tube.FindControl<Border>("PolicyBadge")!.IsVisible);
            await Reply(new("", true, null)); Assert.Equal("first line", text.Text);
            await Reply(new("offline", false, null));
            Assert.Equal(useBrain ? "first line" : "offline", text.Text);
            Assert.Equal(useBrain, tube.FindControl<Border>("AiBadge")!.IsVisible);
            await Reply(new("first line", true, null));
            ai.Pending = new TaskCompletionSource<AiReplyResult>();
            var pending = shell.MakeAutonomyCommentAsync(); Assert.False(pending.IsCompleted);
            if (panic) shell.StopAutonomyForPanic(); else shell.Autonomy.Stop();
            ai.Pending.SetResult(new("completed after stop", true, null));
            await pending; Dispatcher.UIThread.RunJobs();
            // Panic and Stop must suppress a reply that was already in flight.
            Assert.Equal("first line", text.Text);
        }
        finally { shell.RequestExit(); AvApp.Brain = null; AvApp.Ai = null; }
    });

    private sealed class ControlledAi : IAiService
    {
        public AiReplyResult Result = new("", false, null);
        public TaskCompletionSource<AiReplyResult>? Pending;
        public IReadOnlyList<ChatMessage>? Messages;
        public AiCallOptions? Options;
        public int LegacyCalls; public string? Prompt; public bool LegacyInteractive;
        public bool IsAvailable => true;
        public int DailyRequestsRemaining => -1;
        public Task<AiReplyResult> SendAsync(IReadOnlyList<ChatMessage> messages, AiCallOptions options, CancellationToken cancellationToken = default)
        { Messages = messages; Options = options; return Pending?.Task ?? Task.FromResult(Result); }
        public Task<AiReplyResult> GetBambiReplyExAsync(string userInput, bool isUserMessage = false)
        { LegacyCalls++; Prompt = userInput; LegacyInteractive = isUserMessage; return Pending?.Task ?? Task.FromResult(Result); }
        public Task<string> GetBambiReplyAsync(string userInput, bool isUserMessage = false) => throw new NotSupportedException();
        public Task<string?> GetAwarenessReactionAsync(string detectedName, string category, string serviceName = "", string pageTitle = "", TimeSpan? duration = null) => throw new NotSupportedException();
        public Task<string?> GetStillOnReactionAsync(string displayName, string category, TimeSpan duration) => throw new NotSupportedException();
        public Task<string?> GetKeywordCommentAsync(string keyword, string? promptTemplate = null) => throw new NotSupportedException();
        public Task<string?> GetLockScreenReaction(string sentance, int mistakes, int amount, string? promptTemplate = null) => throw new NotSupportedException();
        public Task<string?> GetVideoDoneReaction(string title, string? promptTemplate = null) => throw new NotSupportedException();
        public void Dispose() { }
    }
}

internal sealed class TestSettingsScope : IDisposable
{
    private readonly Func<SettingsService?>? _settings;
    private readonly Func<bool>? _premium;
    public SettingsService Settings { get; }
    public TestSettingsScope()
    {
        _settings = CoreSettings.ServiceProvider; _premium = CoreEntitlement.HasPremiumProvider;
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>().WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        Settings = new SettingsService(); CoreSettings.ServiceProvider = () => Settings;
    }
    public void Dispose() { CoreSettings.ServiceProvider = _settings; CoreEntitlement.HasPremiumProvider = _premium; }
}
