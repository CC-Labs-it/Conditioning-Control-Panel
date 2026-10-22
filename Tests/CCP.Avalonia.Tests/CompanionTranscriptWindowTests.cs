using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel;
using ConditioningControlPanel.Services.Companion.Brain;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>views-companion-transcript-window: WPF Runtime/CompanionTranscriptWindow.cs ShowFor (:121) reads
/// the Core session store, lists only dialogue as You/Her rows in order (:66, :91-113), and the registered
/// panic stop closes it (P06; WPF left it open).</summary>
public sealed class CompanionTranscriptWindowTests
{
    [Fact]
    public Task HistoryListsTheStoredDialogueAndPanicClosesIt() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var store = new CompanionSessionStore();
        var backup = File.Exists(store.SessionPath) ? File.ReadAllText(store.SessionPath) : null;
        var prompt = CoreSettings.Current.CompanionPrompt;
        var memoryWas = prompt.ChatMemoryEnabled;
        try
        {
            prompt.ChatMemoryEnabled = true;   // Core Save/Load are no-ops when off (CompanionSessionStore.cs:117,251)
            store.Save(new[]
            {
                CompanionTurn.Create(TurnKind.UserChat, "first from me"),
                CompanionTurn.Create(TurnKind.AssistantChat, "reply from her"),
            });

            CompanionTranscriptWindow.ShowFor(null);
            Dispatcher.UIThread.RunJobs();
            var w = CompanionTranscriptWindow.LastShown!;
            Assert.True(w.IsVisible);
            var texts = w.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
            Assert.Equal(new[]
            {
                Loc.Get("companion_chat_history_title"),
                Loc.Get("companion_chat_history_you"), "first from me",
                Loc.Get("companion_chat_history_her"), "reply from her",
                Loc.Get("companion_memory_storage_note"),
            }, texts);

            PanicSurfaces.All.Single(x => x.Id == "companion-transcript").Stop(null);
            Dispatcher.UIThread.RunJobs();
            Assert.False(w.IsVisible);
            PanicSurfaces.All.Single(x => x.Id == "companion-transcript").Stop(null);   // idempotent when idle
        }
        finally
        {
            CompanionTranscriptWindow.LastShown?.Close();
            Dispatcher.UIThread.RunJobs();
            if (backup is null) File.Delete(store.SessionPath); else File.WriteAllText(store.SessionPath, backup);
            prompt.ChatMemoryEnabled = memoryWas;
        }
        return Task.CompletedTask;
    });
}
