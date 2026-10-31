using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class TabTraceFailureTests
{
    private sealed class BrokenWriter : StringWriter
    {
        internal int Attempts;
        public override void WriteLine(string? value)
        { Attempts++; throw new IOException("test sink failure"); }
    }

    [Fact]
    public void FailedConsoleSinkIsNotRetried() => AvaloniaTestDispatcher.Run(() =>
    {
        var output = Console.Out;
        var oldTrace = Environment.GetEnvironmentVariable("CCP_TRACE_TAB_SWITCH");
        var oldPath = Environment.GetEnvironmentVariable("CCP_TRACE_TAB_SWITCH_FILE");
        using var broken = new BrokenWriter();
        try
        {
            Environment.SetEnvironmentVariable("CCP_TRACE_TAB_SWITCH", "1");
            Environment.SetEnvironmentVariable("CCP_TRACE_TAB_SWITCH_FILE", null);
            Console.SetOut(broken);
            using (var trace = TabWorkTrace.Start("console")) trace!.Mark("refresh");
            using (var trace = TabWorkTrace.Start("next")) trace!.Mark("refresh");
            Assert.Equal(1, broken.Attempts);
        }
        finally
        {
            Console.SetOut(output);
            Environment.SetEnvironmentVariable("CCP_TRACE_TAB_SWITCH", oldTrace);
            Environment.SetEnvironmentVariable("CCP_TRACE_TAB_SWITCH_FILE", oldPath);
        }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedFileSinkCannotAbortNavigationOrRefresh(bool parentIsFile) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null) RenderProof.EnsureSetUp();
        var oldTrace = Environment.GetEnvironmentVariable("CCP_TRACE_TAB_SWITCH");
        var oldPath = Environment.GetEnvironmentVariable("CCP_TRACE_TAB_SWITCH_FILE");
        var directory = Directory.CreateTempSubdirectory("ccp-trace-failure-");
        var parent = Path.Combine(directory.FullName, "parent");
        var path = Path.Combine(parent, "trace.log");
        if (parentIsFile) File.WriteAllText(parent, "blocks the directory");
        MainShellWindow? shell = null;
        var folders = CoreSettings.Current.BlinkTrainerFolders;
        var savedFolders = folders.ToArray();
        try
        {
            Environment.SetEnvironmentVariable("CCP_TRACE_TAB_SWITCH", "1");
            Environment.SetEnvironmentVariable("CCP_TRACE_TAB_SWITCH_FILE", path);
            shell = new MainShellWindow(); // startup also refreshes Programs
            shell.Show();
            Assert.True(PerfBench.Show(shell, "ProgramsTab"));
            Assert.NotEmpty(shell.Named<ProgramsTabView>("ProgramsTab")!.FindControl<ListBox>("ProgramLibraryList")!.Items);
            Assert.True(PerfBench.Show(shell, "BlinkTrainerTab"));
            folders.Clear();
            folders.Add(directory.FullName);
            shell.Named<BlinkTrainerTabView>("BlinkTrainerTab")!.Refresh();
            Assert.Single(shell.Named<BlinkTrainerTabView>("BlinkTrainerTab")!.FindControl<StackPanel>("BlinkTrainerFolderCardsHost")!.Children);
            // Repair the path: the failed sink must stay disabled, including Dispose and new traces.
            if (parentIsFile) File.Delete(parent);
            Directory.CreateDirectory(parent);
            using (var trace = TabWorkTrace.Start("retry")) trace!.Mark("refresh-completed");
            Assert.False(File.Exists(path));
            var healthy = Path.Combine(directory.FullName, "healthy.log");
            Environment.SetEnvironmentVariable("CCP_TRACE_TAB_SWITCH_FILE", healthy);
            using (var trace = TabWorkTrace.Start("healthy")) trace!.Mark("still-enabled");
            Assert.Contains("healthy end", File.ReadAllText(healthy));
        }
        finally
        {
            shell?.Close();
            folders.Clear();
            folders.AddRange(savedFolders);
            Environment.SetEnvironmentVariable("CCP_TRACE_TAB_SWITCH", oldTrace);
            Environment.SetEnvironmentVariable("CCP_TRACE_TAB_SWITCH_FILE", oldPath);
            directory.Delete(true);
        }
    });
}
