using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.AIService;
using ConditioningControlPanel.Services.Companion.Brain;
using AvApp = ConditioningControlPanel.Avalonia.App;
using WireMessage = ConditioningControlPanel.Services.AIService.ChatMessage;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Companion;
using ConditioningControlPanel.Services.Moderation;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class CompanionSync7EditorsTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.RenderProof.EnsureSetUp();
    }

    private static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [Fact]
    public Task DisabledCustomPromptShowsRunningPersonaAndPreservesStoredText() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        try
        {
            var s = CoreSettings.Current;
            s.ActivePersonalityPresetId = PersonalityPresets.NeutralDefaultId;
            s.CompanionPrompt.UseCustomPrompt = false;
            s.CompanionPrompt.Personality = "old custom voice";
            s.CompanionPrompt.ContextReactions = "old custom reactions";
            var defaults = ActivePersonaPromptText.Resolve(PersonalityService.Shared.GetActivePreset().PromptSettings,
                CompanionPromptSettings.GetDefaults());
            var editor = new CompanionPromptEditorDialog();
            Assert.Equal(defaults.Personality, editor.FindControl<TextBox>("TxtPersonality")!.Text);
            Assert.Equal(defaults.ContextReactions, editor.FindControl<TextBox>("TxtContextReactions")!.Text);
            Click(editor.FindControl<Button>("BtnSave")!);
            Assert.Equal("old custom voice", s.CompanionPrompt.Personality);
            Assert.Equal("old custom reactions", s.CompanionPrompt.ContextReactions);

            var fork = new CompanionPromptEditorDialog();
            fork.FindControl<CheckBox>("ChkUseCustom")!.IsChecked = true;
            fork.FindControl<TextBox>("TxtContextReactions")!.Text = "my new reactions";
            Click(fork.FindControl<Button>("BtnSave")!);
            Assert.True(s.CompanionPrompt.UseCustomPrompt);
            Assert.Equal("", s.CompanionPrompt.Personality);
            Assert.Equal("my new reactions", s.CompanionPrompt.ContextReactions);
        }
        finally { CoreSettings.ServiceProvider = null; }
        return Task.CompletedTask;
    });

    [Fact]
    public Task SilentMarksFollowRecordedLookupAndUnspokenListsStayUnmarked() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var data = new Dictionary<string, bool> { ["VOICED"] = true, ["SILENT"] = true };
        var editor = new TextEditorDialog("spoken", data);
        var list = editor.FindControl<ItemsControl>("ItemList")!;
        var rows = list.ItemsSource!.Cast<TextItem>().ToList();
        Assert.All(rows, row => Assert.False(row.IsSilent));
        Assert.False(editor.FindControl<TextBlock>("TxtSilentHint")!.IsVisible);
        editor.MarkSilentLines(text => text == "VOICED");
        Assert.False(rows.Single(r => r.Text == "VOICED").IsSilent);
        Assert.True(rows.Single(r => r.Text == "SILENT").IsSilent);
        Assert.True(editor.FindControl<TextBlock>("TxtSilentHint")!.IsVisible);

        editor.FindControl<TextBox>("TxtNewItem")!.Text = "ADDED";
        Click(editor.FindControl<Button>("BtnAdd")!);
        Assert.True(list.ItemsSource!.Cast<TextItem>().Single(r => r.Text == "ADDED").IsSilent);
        editor.MarkSilentLines(_ => true);
        Assert.All(list.ItemsSource!.Cast<TextItem>(), row => Assert.False(row.IsSilent));
        Assert.False(editor.FindControl<TextBlock>("TxtSilentHint")!.IsVisible);
        Click(editor.FindControl<Button>("BtnSave")!);
        Dispatcher.UIThread.RunJobs();
        return Task.CompletedTask;
    });

    [Fact]
    public Task WhisperHintOnlyCountsEnabledLinesAndSwitch() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        Window? owner = null;
        try
        {
            var s = CoreSettings.Current;
            s.SubliminalPool = new Dictionary<string, bool> { ["missing-clip-" + Guid.NewGuid()] = false };
            s.SubAudioEnabled = true;
            var feature = new SubliminalFeatureControl();
            var hint = feature.FindControl<TextBlock>("TxtWhispersSilentHint")!;
            Assert.False(hint.IsVisible);
            s.SubliminalPool[s.SubliminalPool.Keys.Single()] = true;
            feature.RefreshSilentHint();
            Assert.True(hint.IsVisible);
            owner = new Window { Content = feature };
            owner.Show();
            Click(feature.FindControl<Button>("BtnManageMessages")!);
            Dispatcher.UIThread.RunJobs();
            var editor = owner.OwnedWindows.OfType<TextEditorDialog>().Single();
            Assert.True(editor.FindControl<ItemsControl>("ItemList")!.ItemsSource!.Cast<TextItem>().Single().IsSilent);
            editor.Close(false);
            owner.Close();
            feature.FindControl<CheckBox>("ChkWhispers")!.IsChecked = false;
            Assert.False(hint.IsVisible);
        }
        finally { owner?.Close(); CoreSettings.ServiceProvider = null; }
        return Task.CompletedTask;
    });

    [Fact]
    public Task SpiceToggleReachesEveryPersonaAndTubeChatUsesLongReplyRules() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = CoreSettings.Current;
        s.AvatarEnabled = true;
        s.MotionLevel = MotionLevel.Off;
        s.AvatarMuted = false;
        s.AiChatEnabled = true;
        s.UseCompanionBrain = true;
        s.CompanionPrompt.UseCustomPrompt = false;
        ExplicitContentGate.MarkAcknowledged(s.CompanionPrompt);
        s.CompanionPrompt.ChatMemoryEnabled = false;
        s.CompanionPrompt.AllowAiToControlEffects = false;
        var shell = new MainShellWindow();
        var dir = Directory.CreateTempSubdirectory("ccp-sync7-spice-").FullName;
        var (priorAi, priorBrain) = (AvApp.Ai, AvApp.Brain);
        using var memory = new MemoryStore(Path.Combine(dir, "memory.json"));
        var wire = new CaptureAi();
        using var brain = new CompanionBrain(wire, memory: memory,
            store: new CompanionSessionStore(Path.Combine(dir, "session.json"), Path.Combine(dir, "legacy.json")),
            preview: () => false);
        (AvApp.Ai, AvApp.Brain) = (wire, brain);
        try
        {
            // ponytail: exercise the real shell/tube behavior without starting the unrelated shell FX/layout.
            shell.ShowAvatarTube();
            var tube = shell.Tube!;
            foreach (var preset in PersonalityService.Shared.GetAllPresets().Where(p => !p.RequiresExplicitAcknowledgement))
            {
                s.ActivePersonalityPresetId = preset.Id;
                s.SlutModeEnabled = false;
                Assert.True(ExplicitContentGate.IsAlreadyAcknowledged(CoreSettings.Current.CompanionPrompt));
                Assert.True(await shell.SetSlutModeAsync(true).WaitAsync(TimeSpan.FromSeconds(15)));
                Assert.Contains(SlutModeBite.Block, new BambiSprite().GetSystemPrompt());
            }
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(ConditioningControlPanel.Localization.Loc.Get("companion_spice_on_line"),
                tube.FindControl<TextBlock>("TxtSpeech")!.Text);
            tube.FindControl<TextBox>("TxtUserInput")!.Text = "hello";
            await tube.SendChatAsync().WaitAsync(TimeSpan.FromSeconds(30));
            Assert.NotNull(wire.Options);
            Assert.Equal(AiCallOptions.ChatSpice.MaxTokens, wire.Options!.MaxTokens);
            Assert.Contains(PromptAssembler.ChatSpiceInstruction, wire.Messages![0].Content);
            Assert.Contains(SlutModeBite.Header, wire.Messages[0].Content);
        }
        finally
        {
            shell.Close();
            Dispatcher.UIThread.RunJobs();
            (AvApp.Ai, AvApp.Brain) = (priorAi, priorBrain);
            brain.Dispose();
            memory.Dispose();
            CoreSettings.ServiceProvider = null;
            Directory.Delete(dir, true);
        }
    });

    private sealed class CaptureAi : IAiService
    {
        internal AiCallOptions? Options;
        internal IReadOnlyList<WireMessage>? Messages;
        public bool IsAvailable => true;
        public int DailyRequestsRemaining => 100;
        public Task<AiReplyResult> SendAsync(IReadOnlyList<WireMessage> messages, AiCallOptions options, CancellationToken cancellationToken = default)
        {
            Options = options; Messages = messages;
            return Task.FromResult(new AiReplyResult("Hello there.", true, null));
        }
        public Task<string> GetBambiReplyAsync(string input, bool isUserMessage = false) => throw new NotSupportedException();
        public Task<AiReplyResult> GetBambiReplyExAsync(string input, bool isUserMessage = false) => throw new NotSupportedException();
        public Task<string?> GetAwarenessReactionAsync(string name, string category, string serviceName = "", string pageTitle = "", TimeSpan? duration = null) => throw new NotSupportedException();
        public Task<string?> GetStillOnReactionAsync(string name, string category, TimeSpan duration) => throw new NotSupportedException();
        public Task<string?> GetKeywordCommentAsync(string keyword, string? promptTemplate = null) => throw new NotSupportedException();
        public Task<string?> GetLockScreenReaction(string sentence, int mistakes, int amount, string? promptTemplate = null) => throw new NotSupportedException();
        public Task<string?> GetVideoDoneReaction(string title, string? promptTemplate = null) => throw new NotSupportedException();
        public void Dispose() { }
    }
}
