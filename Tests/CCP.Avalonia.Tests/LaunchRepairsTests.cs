using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.AIService;
using ConditioningControlPanel.Services.Moderation;
using System.Linq;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public class LaunchRepairsTests
{
    [Fact]
    public void ReinstallUsesLiveAiAvailability()
    {
        var settings = new SettingsService();
        var previousSettings = CoreSettings.ServiceProvider;
        var previousProvider = CoreAi.IsAvailableProvider;
        var previousAi = AvApp.Ai;
        CoreSettings.ServiceProvider = () => settings;
        AvApp.Ai = new AvailableAi();
        CoreAi.IsAvailableProvider = null;
        try
        {
            var preset = new KeywordTriggerPreset { Id = "ai-launch-test", IsBuiltIn = true };
            preset.Triggers.Add(new KeywordTrigger { Id = "ai", Actions = { new AvatarCommentAction { Enabled = true, RequireAiAvailable = true } } });
            settings.Current.KeywordTriggerPresets.Add(preset);
            settings.PendingPresetReinstalls.Add(preset.Id);
            AvApp.RepairLaunchSettings(settings);
            var clone = Assert.Single(settings.Current.KeywordTriggers.Where(t => t.Id == "preset:ai-launch-test:ai"));
            Assert.True(Assert.IsType<AvatarCommentAction>(Assert.Single(clone.Actions)).Enabled);
        }
        finally
        {
            AvApp.Ai = previousAi;
            CoreAi.IsAvailableProvider = previousProvider;
            CoreSettings.ServiceProvider = previousSettings;
        }
    }

    [Fact]
    public void RestoredCloudAccountKeepsAiActionsEnabledOnReinstall()
    {
        var app = File.ReadAllText(Path.Combine(Root(), "CCP.Avalonia", "App.axaml.cs"));
        var restore = app.IndexOf("Platform.AccountSeed.RestoreSession();", StringComparison.Ordinal);
        var repair = app.IndexOf("RepairLaunchSettings(Settings);", StringComparison.Ordinal);
        Assert.True(restore > 0 && repair > restore, "restore the account before reinstalling presets");

        var settings = new SettingsService();
        var oldSettings = CoreSettings.ServiceProvider;
        var oldGet = CoreSecrets.RetrieveProvider;
        var oldId = CoreAccount.UnifiedUserId;
        var oldAi = AvApp.Ai;
        var oldAvailable = CoreAi.IsAvailableProvider;
        try
        {
            CoreSettings.ServiceProvider = () => settings;
            CoreSecrets.RetrieveProvider = _ => "test-token";
            CoreAccount.UnifiedUserId = null;
            settings.Current.UnifiedId = "test-user";
            AvApp.Ai = new AiServiceStrategy();
            var preset = new KeywordTriggerPreset { Id = "cloud-launch-test", IsBuiltIn = true };
            preset.Triggers.Add(new KeywordTrigger { Id = "ai", Actions = { new AvatarCommentAction { Enabled = true, RequireAiAvailable = true } } });
            settings.Current.KeywordTriggerPresets.Add(preset);
            settings.PendingPresetReinstalls.Add(preset.Id);
            ConditioningControlPanel.Avalonia.Platform.AccountSeed.RestoreSession();
            AvApp.RepairLaunchSettings(settings);
            var clone = Assert.Single(settings.Current.KeywordTriggers.Where(t => t.Id == "preset:cloud-launch-test:ai"));
            Assert.True(Assert.IsType<AvatarCommentAction>(Assert.Single(clone.Actions)).Enabled);
        }
        finally
        {
            CoreSettings.ServiceProvider = oldSettings;
            CoreSecrets.RetrieveProvider = oldGet;
            CoreAccount.UnifiedUserId = oldId;
            AvApp.Ai = oldAi;
            CoreAi.IsAvailableProvider = oldAvailable;
        }
    }

    [Fact]
    public void DesktopHealsJournalBeforeRepairCanSave()
    {
        var app = File.ReadAllText(Path.Combine(Root(), "CCP.Avalonia", "App.axaml.cs"));
        var heal = app.IndexOf("SessionSettingsJournal.HealOnLaunch(CorePaths.UserData, Settings.Current)", StringComparison.Ordinal);
        var repair = app.IndexOf("RepairLaunchSettings(Settings);", StringComparison.Ordinal);
        Assert.True(heal > 0 && repair > heal, "heal the journal before any launch repair can save");
    }

    private static string Root([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));

    private sealed class AvailableAi : IAiService
    {
        public bool IsAvailable => true;
        public int DailyRequestsRemaining => 1;
        public Task<AiReplyResult> SendAsync(IReadOnlyList<ChatMessage> messages, AiCallOptions options, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string> GetBambiReplyAsync(string input, bool isUserMessage = false) => throw new NotSupportedException();
        public Task<AiReplyResult> GetBambiReplyExAsync(string input, bool isUserMessage = false) => throw new NotSupportedException();
        public Task<string?> GetAwarenessReactionAsync(string name, string category, string serviceName = "", string pageTitle = "", TimeSpan? duration = null) => throw new NotSupportedException();
        public Task<string?> GetStillOnReactionAsync(string name, string category, TimeSpan duration) => throw new NotSupportedException();
        public Task<string?> GetKeywordCommentAsync(string keyword, string? promptTemplate = null) => throw new NotSupportedException();
        public Task<string?> GetLockScreenReaction(string sentence, int mistakes, int amount, string? promptTemplate = null) => throw new NotSupportedException();
        public Task<string?> GetVideoDoneReaction(string title, string? promptTemplate = null) => throw new NotSupportedException();
        public void Dispose() { }
    }

    [Fact]
    public void RepairsCustomSourcesAndReinstallsQueuedPresets()
    {
        var settings = new SettingsService();
        var previous = CoreSettings.ServiceProvider;
        var previousAiProvider = CoreAi.IsAvailableProvider;
        CoreSettings.ServiceProvider = () => settings;
        try
        {
            var custom = new KeywordTriggerPreset { Id = "custom-test", IsBuiltIn = false, MasterEnabled = true };
            custom.Triggers.Add(new KeywordTrigger { Id = "old", Keyword = "old" });
            settings.Current.KeywordTriggerPresets.Add(custom);
            settings.Current.KeywordTriggers.Add(new KeywordTrigger { Id = "preset:custom-test:edited", Keyword = "edited" });
            var builtIn = new KeywordTriggerPreset { Id = "builtin-test", IsBuiltIn = true };
            builtIn.Triggers.Add(new KeywordTrigger { Id = "new", Keyword = "new", Actions = { new ChasterAddTimeAction { Enabled = true } } });
            settings.Current.KeywordTriggerPresets.Add(builtIn);
            settings.Current.KeywordTriggers.Add(new KeywordTrigger { Id = "preset:builtin-test:old", Actions = { new ChasterAddTimeAction { Enabled = true } } });
            settings.PendingPresetReinstalls.Add(builtIn.Id);
            AvApp.RepairLaunchSettings(settings);
            Assert.Equal("edited", Assert.Single(custom.Triggers).Keyword);
            var clone = Assert.Single(settings.Current.KeywordTriggers.Where(t => t.Id == "preset:builtin-test:new"));
            Assert.True(Assert.IsType<ChasterAddTimeAction>(Assert.Single(clone.Actions)).Enabled);
            Assert.Empty(settings.PendingPresetReinstalls);
        }
        finally { CoreAi.IsAvailableProvider = previousAiProvider; CoreSettings.ServiceProvider = previous; }
    }
}
