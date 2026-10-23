using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Deeper;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Descent;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class GuardedMonoConsumerTests
{
    [Fact]
    public Task RailsMoodPlayerAndShellUseTheGuardedResourceAndFollowReplacement() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var app = Application.Current!;
        var original = app.Resources[FontGuard.MonoResourceKey];
        var guard = new FontGuard(name => { if (name == "Cascadia Mono") throw new UnauthorizedAccessException(); });
        guard.Verify(app.Resources, windows: true);
        var guarded = Assert.IsType<FontFamily>(app.Resources[FontGuard.MonoResourceKey]);
        Assert.Equal(new[] { "Consolas", "Courier New" }, guarded.FamilyNames);
        var fuse = new DescentFuseRailChip();
        var chaster = new ChasterRailChip();
        var mood = new CircesMoodMeter();
        var host = new Window { Content = new StackPanel { Children = { fuse, chaster, mood } } };
        var shell = new MainShellWindow();
        var player = new EnhancementPlayerWindow(null, null);
        var tipHost = new Window();
        try
        {
            foreach (var window in new Window[] { host, shell, player })
            {
                window.ApplyStyling();
                foreach (var control in window.GetLogicalDescendants().OfType<Control>()) control.ApplyStyling();
            }
            shell.ApplyFusePhase(DescentFusePhase.Clock);
            fuse.Apply(DescentFusePhase.Clock);
            var sparkTip = Assert.IsType<Border>(ToolTip.GetTip(shell.Named<Grid>("FuseSparkHost")!));
            var railTip = Assert.IsType<Border>(ToolTip.GetTip(fuse));
            // A closed tip has no parent, so a DynamicResource cannot resolve yet. An open tooltip parents it
            // under the adorned control; a stand-in host gives it the same app-resource lookup.
            tipHost.Content = new StackPanel { Children = { sparkTip, railTip } };
            tipHost.ApplyStyling();
            var labels = fuse.GetLogicalDescendants().OfType<TextBlock>()
                .Concat(chaster.GetLogicalDescendants().OfType<TextBlock>().Where(t => t.FontSize is 10 or 8.5))
                .Concat(mood.Children.OfType<TextBlock>().Where(t => t.FontSize != 13))
                .Concat(sparkTip.GetLogicalDescendants().OfType<TextBlock>().Where(t => t.FontSize == 14))
                .Concat(railTip.GetLogicalDescendants().OfType<TextBlock>().Where(t => t.FontSize == 15))
                .Append(shell.Named<TextBlock>("FuseCornerDigits")!)
                .Append(player.FindControl<TextBlock>("TxtMiniHoverTime")!).ToArray();
            Assert.Equal(9, labels.Length); // no empty/vacuous consumer walk
            Dispatcher.UIThread.RunJobs();
            Assert.All(labels, label => Assert.Equal(guarded, label.FontFamily));
            var replacement = new FontFamily(AppFonts.Mono);
            app.Resources[FontGuard.MonoResourceKey] = replacement;
            Dispatcher.UIThread.RunJobs();
            Assert.All(labels, label => Assert.Equal(replacement, label.FontFamily));
        }
        finally
        {
            host.Close();
            shell.Close();
            player.Close();
            tipHost.Close();
            app.Resources[FontGuard.MonoResourceKey] = original;
        }
        return Task.CompletedTask;
    });
}
