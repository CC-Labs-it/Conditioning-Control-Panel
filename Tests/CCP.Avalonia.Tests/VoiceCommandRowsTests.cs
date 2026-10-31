using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.LogicalTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class VoiceCommandRowsTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public Task QuickToysShowsOnlyRunnablePhrasesAndRestoresThem(bool freeze, bool shake, bool count) =>
        AvaloniaTestDispatcher.RunAsync(() =>
        {
            AmbientBeatLoopsTests.Setup();
            var page = new SheListeningTabView();
            var head = page.GetLogicalDescendants().OfType<IconLabel>()
                .Single(h => Equals(h.Tag, "voice:freeze_once|shake_once|count_once"));
            var panel = Assert.IsAssignableFrom<Panel>(head.Parent);
            var description = Assert.IsType<TextBlock>(panel.Children[panel.Children.IndexOf(head) + 1]);
            var all = new[] { "freeze_once", "shake_once", "count_once" };
            var requested = all.Where((_, i) => new[] { freeze, shake, count }[i]).ToArray();
            // WPF SheListeningTabView.xaml:536-540: three distinct phrase lines in one group.
            // Reconciliation must restore previously removed lines and hide the whole empty group.
            foreach (var available in new[] { all, requested, Array.Empty<string>(), requested })
            {
                page.ShowOnlyVoiceCommands(available.ToHashSet());
                Assert.Equal(available.Length > 0, head.IsVisible);
                Assert.Equal(head.IsVisible, description.IsVisible);
                var text = string.Concat(description.Inlines!.OfType<Run>().Select(r => r.Text));
                Assert.Equal(available.Contains("freeze_once"), text.Contains("freeze me"));
                Assert.Equal(available.Contains("shake_once"), text.Contains("shake the screen"));
                Assert.Equal(available.Contains("count_once"), text.Contains("count for me"));
            }
            return Task.CompletedTask;
        });
}
