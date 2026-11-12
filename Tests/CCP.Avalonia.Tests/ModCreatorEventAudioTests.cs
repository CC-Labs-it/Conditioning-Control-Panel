using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Automation;
using Avalonia.Interactivity;
using ConditioningControlPanel;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ModCreatorEventAudioTests
{
    [Fact]
    public void ImportExportLoadAndResetRoundTrip() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current == null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia().WithInterFont()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var root = Path.Combine(Path.GetTempPath(), "ccp-event-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        var w = new ModCreatorWindow();
        try
        {
            var input = Path.Combine(root, "input"); Directory.CreateDirectory(input);
            File.WriteAllText(Path.Combine(input, "Hello World.mp3"), "audio");
            w.ImportEventAudioFrom(input);
            var panel = (StackPanel)typeof(ModCreatorWindow).GetField("_eventAudioRowsPanel", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(w)!;
            Assert.Single(panel.Children);
            Assert.Contains(panel.GetLogicalDescendants().OfType<TextBlock>(), x => x.Text != null && x.Text.Contains("clean slug"));
            void Hook(string name, params object[] args) => typeof(ModCreatorWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(w, args);
            var output = Path.Combine(root, "output"); Hook("WriteSideFilesTo", output);
            Assert.Equal("audio", File.ReadAllText(Path.Combine(output, "sounds", "event_audio", "hello world.mp3")));
            Hook("ClearSideFileState"); Assert.Empty(panel.Children);
            Hook("LoadSideFilesFrom", output); Assert.Single(panel.Children);
            Assert.True(panel.GetLogicalDescendants().OfType<TextBox>().Single().IsReadOnly);
            Hook("ClearSideFileState"); Assert.Empty(panel.Children);

            var add = w.GetLogicalDescendants().OfType<Button>().Single(b => Equals(b.Content, "+ Add line"));
            add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Single(panel.Children);
            var free = panel.GetLogicalDescendants().OfType<TextBox>().Single();
            Assert.False(free.IsReadOnly);
            free.Text = "A phrase!";
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(panel.GetLogicalDescendants().OfType<TextBlock>(), x => x.Text == "a phrase.mp3");
            var remove = panel.GetLogicalDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Remove line");
            remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Empty(panel.Children);

            w.ImportEventAudioFrom(input);
            var play = panel.GetLogicalDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Play preview");
            Assert.True(play.IsVisible);
            var oldPlayer = CoreAudio.PlayStoppableProvider;
            string? played = null;
            var stops = 0;
            CoreAudio.PlayStoppableProvider = (path, _, _, _, _) => { played = path; return () => stops++; };
            try
            {
                play.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(Path.Combine(input, "Hello World.mp3"), played);
                play.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(1, stops);
            }
            finally { CoreAudio.PlayStoppableProvider = oldPlayer; }
        }
        finally { w.Close(); Dispatcher.UIThread.RunJobs(); Directory.Delete(root, true); }
    });
}
