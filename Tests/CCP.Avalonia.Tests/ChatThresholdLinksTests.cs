using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Companion;
using ConditioningControlPanel.Services.Companion.Brain;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class ChatThresholdLinksTests
{
    internal static void Setup()
    {
        if (Application.Current is null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    [Fact]
    public Task AssistantTitleGetsAVisibleChipButUserAndEchoDoNot() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var previousSettings = CoreSettings.ServiceProvider;
        var previousBrain = AvApp.Brain;
        var previousAccount = CoreAccount.UnifiedUserId;
        var previousNavigate = CompanionLinkLauncher.Navigate;
        var directory = Directory.CreateTempSubdirectory("ccp-threshold-links-").FullName;
        ChatThresholdViewModel? vm = null;
        Window? window = null;
        try
        {
            var settings = new SettingsService();
            CoreSettings.ServiceProvider = () => settings;
            CoreSettings.Current.UseCompanionBrain = true;
            CoreSettings.Current.AiChatEnabled = true;
            CoreAccount.UnifiedUserId = "threshold-link-test";
            CoreSettings.Current.CompanionPrompt.AiProvider = AiProviderType.Cloud;
            CoreSettings.Current.GlobalKnowledgeBaseLinks = new List<KnowledgeBaseLink>
            {
                new() { Title = "Threshold link test video", Url = "https://127.0.0.1/threshold-video" }
            };
            CompanionLinkIndex.ResetForTests();
            using var brain = new CompanionBrain(new AiService("http://127.0.0.1:1"),
                memory: new MemoryStore(Path.Combine(directory, "memory.json")),
                store: new CompanionSessionStore(Path.Combine(directory, "session.json"), Path.Combine(directory, "legacy.json")));
            AvApp.Brain = brain;
            foreach (var kind in new[] { TurnKind.UserChat, TurnKind.BarkEcho, TurnKind.AssistantChat })
                brain.Session.Append(kind, "Watch Threshold link test video for me.");
            vm = ChatThresholdViewModel.CreateLive();
            Assert.Equal(3, vm.Turns.Count);
            Assert.Null(vm.Turns[0].LinkTitle);
            Assert.Null(vm.Turns[1].LinkTitle);
            Assert.Equal("Threshold link test video", vm.Turns[2].LinkTitle);
            Assert.NotNull(vm.Turns[2].OpenLinkCommand);
            var view = new ChatThresholdView { ViewModel = vm };
            window = new Window { Width = 700, Height = 600, Content = view };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var chips = view.GetVisualDescendants().OfType<Button>().Where(b => b.Name == "WatchChip").ToList();
            var chip = Assert.Single(chips, b => b.IsVisible);
            string? navigated = null;
            CompanionLinkLauncher.Navigate = url => { navigated = url; return true; };
            var point = chip.TranslatePoint(new Point(chip.Bounds.Width / 2, chip.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Assert.Equal("https://127.0.0.1/threshold-video", navigated);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(16);
            Dispatcher.UIThread.RunJobs();
            using var frame = window.GetLastRenderedFrame();
            Assert.NotNull(frame);
            frame.Save(Path.Combine(CorePaths.UserData, "ccp-chat-threshold-link-chip.png"), PngBitmapEncoderOptions.Default);
            return Task.CompletedTask;
        }
        finally
        {
            window?.Close();
            vm?.Detach();
            AvApp.Brain = previousBrain;
            CoreAccount.UnifiedUserId = previousAccount;
            CompanionLinkLauncher.Navigate = previousNavigate;
            CoreSettings.ServiceProvider = previousSettings;
            CompanionLinkIndex.ResetForTests();
        }
    });

    // WPF attaches CompanionWheelRelay to ThreadList; this head relies on Avalonia's native scroll chaining
    // instead, so a notch past the thread's end must still move the page.
    [Fact]
    public Task WheelPastTheThreadEndScrollsThePage() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var previousSettings = CoreSettings.ServiceProvider;
        var previousBrain = AvApp.Brain;
        var previousAccount = CoreAccount.UnifiedUserId;
        var directory = Directory.CreateTempSubdirectory("ccp-threshold-wheel-").FullName;
        ChatThresholdViewModel? vm = null;
        Window? window = null;
        try
        {
            var settings = new SettingsService();
            CoreSettings.ServiceProvider = () => settings;
            CoreSettings.Current.UseCompanionBrain = true;
            CoreSettings.Current.AiChatEnabled = true;
            CoreAccount.UnifiedUserId = "threshold-wheel-test";
            CoreSettings.Current.CompanionPrompt.AiProvider = AiProviderType.Cloud;
            using var brain = new CompanionBrain(new AiService("http://127.0.0.1:1"),
                memory: new MemoryStore(Path.Combine(directory, "memory.json")),
                store: new CompanionSessionStore(Path.Combine(directory, "session.json"), Path.Combine(directory, "legacy.json")));
            AvApp.Brain = brain;
            var longLine = string.Join(' ', Enumerable.Repeat("a long line that wraps across the thread", 20));
            for (var i = 0; i < 3; i++) brain.Session.Append(TurnKind.AssistantChat, longLine);
            vm = ChatThresholdViewModel.CreateLive();
            var view = new ChatThresholdView { ViewModel = vm };
            var page = new ScrollViewer
            {
                Content = new StackPanel { Children = { view, new Border { Height = 2000 } } }
            };
            window = new Window { Width = 700, Height = 600, Content = page };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var thread = view.GetVisualDescendants().OfType<ItemsControl>().Single(c => c.Name == "ThreadList");
            var inner = thread.GetVisualDescendants().OfType<ScrollViewer>().First();
            Assert.True(inner.Extent.Height > inner.Viewport.Height, "thread must overflow for this test");
            inner.ScrollToEnd();
            Dispatcher.UIThread.RunJobs();
            var innerEnd = inner.Offset.Y;
            Assert.Equal(0, page.Offset.Y);
            var point = thread.TranslatePoint(new Point(thread.Bounds.Width / 2, thread.Bounds.Height / 2), window)!.Value;
            Assert.True(point.Y > 0 && point.Y < window.Height, $"thread centre {point} off screen");
            window.MouseWheel(point, new Vector(0, -1));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(innerEnd, inner.Offset.Y);
            Assert.True(page.Offset.Y > 0, "the page did not scroll: the thread swallowed the wheel");
            return Task.CompletedTask;
        }
        finally
        {
            window?.Close();
            vm?.Detach();
            AvApp.Brain = previousBrain;
            CoreAccount.UnifiedUserId = previousAccount;
            CoreSettings.ServiceProvider = previousSettings;
        }
    });
}
