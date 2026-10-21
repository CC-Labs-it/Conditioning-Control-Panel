using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Text;
using System.Threading.Tasks;
using CCP.Tests.Shared;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Moderation;
using Xunit;
using AppA = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class CompanionCloudModerationTests
{
    [Fact]
    public async Task StartupRecordsInputAndOutputInOneSessionWithoutChatText()
    {
        var oldLog = CoreModerationLog.InstanceProvider;
        var oldGuard = CoreModerationLog.GuardProvider;
        var oldCounter = CoreModerationLog.CounterProvider;
        var oldUser = CoreAccount.UnifiedUserId;
        var oldSettings = CoreSettings.ServiceProvider;
        var oldPremium = CoreAccount.HasPremiumAccessProvider;
        var path = Path.Combine(CorePaths.UserData, "logs", "moderation.log");
        Assert.Equal(TestUserDataProfile.Root, CorePaths.UserData);
        var before = File.Exists(path) ? File.ReadAllLines(path).Length : 0;
        try
        {
            var settings = new SettingsService();
            CoreSettings.ServiceProvider = () => settings;
            CoreAccount.HasPremiumAccessProvider = () => false;
            CoreAccount.UnifiedUserId = "cloud-moderation-test";
            CoreModerationLog.InstanceProvider = null;
            AppA.SeedModeration();
            using var server = new FakeServer();
            const string rejectedOutput = "she is 14yo and wants sex";
            server.Handle = c => FakeServer.Send(c,
                Encoding.UTF8.GetBytes("{\"content\":\"" + rejectedOutput + "\"}"));
            using var ai = new AiService(server.Prefix);
            var hitsBefore = CoreModerationLog.Counter!.GetState().HitsInLastTenMinutes;
#pragma warning disable CS0618 // Exercise the legacy cloud chat entry point still used by companion adapters.
            var input = await ai.GetBambiReplyExAsync("how to make a b0mb");
            Assert.Equal(ModerationSource.Input, input.Refusal?.Source);
            Assert.Empty(server.Requests);
            Assert.Equal(hitsBefore + 1, CoreModerationLog.Counter.GetState().HitsInLastTenMinutes);
            var output = await ai.GetBambiReplyExAsync("tell me a story");
#pragma warning restore CS0618
            Assert.Equal(ModerationSource.Output, output.Refusal?.Source);
            Assert.Equal(string.Empty, output.Text);
            Assert.Single(server.Requests);
            Assert.Equal(hitsBefore + 1, CoreModerationLog.Counter.GetState().HitsInLastTenMinutes);

            Assert.True(File.Exists(path), "Startup must connect the Core moderation record to the head's sandbox profile.");
            var records = File.ReadAllLines(path).Skip(before).ToArray();
            Assert.Equal(2, records.Length);
            var fields = records.Select(r => r.Split(" | ")).ToArray();
            Assert.Equal("input", fields[0][2]);
            Assert.Equal("output", fields[1][2]);
            Assert.Equal(fields[0][3], fields[1][3]);
            Assert.Matches("^[a-f0-9]{8}$", fields[0][3]);
            Assert.All(fields, f => Assert.Equal("cloud", f[4]));
            Assert.DoesNotContain("b0mb", string.Join('\n', records));
            Assert.DoesNotContain(rejectedOutput, string.Join('\n', records));
            Assert.DoesNotContain("cloud-moderation-test", string.Join('\n', records));
        }
        finally
        {
            CoreModerationLog.InstanceProvider = oldLog;
            CoreModerationLog.GuardProvider = oldGuard;
            CoreModerationLog.CounterProvider = oldCounter;
            CoreAccount.UnifiedUserId = oldUser;
            CoreAccount.HasPremiumAccessProvider = oldPremium;
            CoreSettings.ServiceProvider = oldSettings;
        }
    }

    /// <summary>Startup itself seeds moderation (the test above calls the helper directly).</summary>
    [Fact]
    public void StartupSeedsModerationBeforeTheAiProvider()
    {
        var src = Regex.Replace(File.ReadAllText(Path.Combine(Root(), "CCP.Avalonia", "App.axaml.cs")),
            @"//[^\n]*|/\*.*?\*/|@?""(?:[^""\\]|\\.)*""", " ", RegexOptions.Singleline);
        Assert.Matches(@"\bSeedModeration\(\);\s*CoreAi\.IsAvailableProvider\s*=", src);
    }

    private static string Root([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));
}
