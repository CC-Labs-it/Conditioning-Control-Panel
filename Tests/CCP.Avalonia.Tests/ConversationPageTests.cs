using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion.Pages;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion.V2;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Companion.Brain;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>WPF V2/ConversationPage + AiPage name/recap hosts, driven from the shell's Companion tab.</summary>
public sealed class ConversationPageTests
{
    [Fact]
    public System.Threading.Tasks.Task CompanionTabShowsTheSharedConversationAndFollowsTheAccount() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = CoreSettings.Current;
        s.Welcomed = true;
        s.HasAcceptedAgeVerification = true;
        s.SeenFeatureIntros.AddRange(FeatureIntros.All.Keys);   // P52
        s.AwarenessModeEnabled = false;

        var dir = Directory.CreateTempSubdirectory("ccp-conv-").FullName;
        var previousBrain = AvApp.Brain;
        var previousUser = CoreAccount.UnifiedUserId;
        CoreAccount.UnifiedUserId = "conv-A";
        var aPath = MemoryStore.PreviewAccountDirectory("conv-A", dir);
        new CompanionSessionStore(Path.Combine(aPath, "session.json"), Path.Combine(aPath, "no-import.json")).Save(new[]
        {
            CompanionTurn.Create(TurnKind.UserChat, "hello there"),
            CompanionTurn.Create(TurnKind.BarkEcho, "a bark is not a chat line"),
            CompanionTurn.Create(TurnKind.AssistantChat, "hi, cutie")
        });
        var brain = new CompanionBrain(new AiService("http://127.0.0.1:9"), preview: () => true, accountDirectory: dir);
        AvApp.Brain = brain;
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            shell.ShowTab("companion");
            Dispatcher.UIThread.RunJobs();
            var room = shell.GetLogicalDescendants().OfType<CompanionRoomView>().Single();
            var page = room.FindControl<ConversationPage>("ConversationZone")!;

            // The page is a projection of the brain's one conversation: chat turns only, in order.
            Assert.Equal(new[] { "hello there", "hi, cutie" }, page.Vm.Turns.Select(t => t.Text));
            Assert.True(page.Vm.Turns[0].IsUser);

            // An account switch (CoreAccount.UnifiedIdentityChanged) drops the other account's thread and draft.
            page.Vm.Draft = "half typed";
            CoreAccount.UnifiedUserId = "conv-B";
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(page.Vm.Turns);
            Assert.Equal("", page.Vm.Draft);

            // Settings / connection / video links open the Companion pages, as WPF OpenSheet does.
            Assert.Equal("personality", ConversationPage.PageFor("who"));
            Assert.Equal("companionlinks", ConversationPage.PageFor("videos"));
            Assert.Equal("companionai", ConversationPage.PageFor("connection"));

            // Companion > AI carries the preferred-name editor above the diary; saving writes the profile.
            shell.ShowTab("companionai");
            Dispatcher.UIThread.RunJobs();
            var editor = shell.GetLogicalDescendants().OfType<AiPage>().Single()
                .GetLogicalDescendants().OfType<PreferredNameEditor>().Single();
            editor.NameBox.Text = "  Kim   Rose ";
            editor.Save();
            Assert.Equal("Kim Rose", brain.Memory.Profile[MemoryStore.KeyPreferredName]);
            Assert.Equal("Kim Rose", editor.NameBox.Text);
        }
        finally
        {
            shell.Close();
            Dispatcher.UIThread.RunJobs();
            AvApp.Brain = previousBrain;
            CoreAccount.UnifiedUserId = previousUser;
            brain.Dispose();
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
        await System.Threading.Tasks.Task.CompletedTask;
    });
}
