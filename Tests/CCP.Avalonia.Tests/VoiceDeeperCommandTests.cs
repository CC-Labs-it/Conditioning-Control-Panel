using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class VoiceDeeperCommandTests
{
    [Fact]
    public Task DeeperRunsBrainDrainAndTracksTheSeam() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var old = CoreEngine.StartBrainDrain;
        var enabled = CoreSettings.Current.BrainDrainEnabled;
        var player = new BrainDrainPlayer((_, _) => null, new Clock(), () => 0);
        var shell = new MainShellWindow();
        shell.Show();
        try
        {
            CoreEngine.StartBrainDrain = player.Start;
            CoreSettings.Current.BrainDrainEnabled = true;
            Assert.Contains(shell.VoiceCmds.Available, i => i.Name == "deeper");
            Assert.Contains("deeper", shell.VoiceCmds.Grammar());
            Assert.Contains("deeper", shell.VoiceCmds.HelpLine());
            shell.ShowTab("shelistening");
            Dispatcher.UIThread.RunJobs();
            var row = shell.SheListeningPage!.GetLogicalDescendants().OfType<IconLabel>()
                .Single(x => (string?)x.Tag == "voice:deeper");
            Assert.True(row.IsVisible);
            Assert.True(shell.VoiceCmds.TryHandleInlineCommand("hey bambi deeper", new[] { "hey bambi" }));
            Dispatcher.UIThread.RunJobs();
            Assert.True(player.IsRunning);
            player.Stop();
            CoreSettings.Current.BrainDrainEnabled = false;
            Assert.True(shell.VoiceCmds.TryHandleInlineCommand("hey bambi deeper", new[] { "hey bambi" }));
            Dispatcher.UIThread.RunJobs();
            Assert.False(player.IsRunning);
            CoreEngine.StartBrainDrain = null;
            Assert.DoesNotContain(shell.VoiceCmds.Available, i => i.Name == "deeper");
            Assert.DoesNotContain("deeper", shell.VoiceCmds.Grammar());
        }
        finally
        {
            player.Stop(); CoreEngine.StartBrainDrain = old;
            CoreSettings.Current.BrainDrainEnabled = enabled;
            shell.Close();
        }
        return Task.CompletedTask;
    });

    private sealed class Clock : TimeProvider
    {
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) => new Timer();
        private sealed class Timer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
