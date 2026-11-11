using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ChaosCompanionTests
{
    private static void Click(ChaosHubWindow hub, string name) =>
        hub.FindControl<Button>(name)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [Fact]
    public Task Dollhouse_back_and_close_restore_the_companion() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var old = ChaosHubWindow.SetCompanionActive;
        var oldRender = ConditioningControlPanel.Avalonia.RenderProof.Rendering;
        var calls = new List<bool>();
        ChaosHubWindow? hub = null;
        try
        {
            ConditioningControlPanel.Avalonia.RenderProof.Rendering = true;
            ChaosHubWindow.SetCompanionActive = calls.Add;
            hub = new ChaosHubWindow();
            hub.Show();
            calls.Clear();
            Click(hub, "BtnMenuDollhouse");
            Assert.Equal(new[] { true }, calls);
            Click(hub, "BtnBackToMenu");
            Assert.Equal(new[] { true, false }, calls);
            Click(hub, "BtnMenuDollhouse");
            hub.Close();
            Assert.Equal(new[] { true, false, true, false }, calls);
        }
        finally
        {
            hub?.Close();
            ChaosHubWindow.SetCompanionActive = old;
            ConditioningControlPanel.Avalonia.RenderProof.Rendering = oldRender;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task Maximizing_and_restoring_the_menu_changes_the_companion() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var old = ChaosHubWindow.SetCompanionActive;
        var oldRender = ConditioningControlPanel.Avalonia.RenderProof.Rendering;
        var calls = new List<bool>();
        ChaosHubWindow? hub = null;
        try
        {
            ConditioningControlPanel.Avalonia.RenderProof.Rendering = true;
            ChaosHubWindow.SetCompanionActive = calls.Add;
            hub = new ChaosHubWindow();
            hub.Show();
            calls.Clear();
            hub.WindowState = WindowState.Maximized;
            hub.WindowState = WindowState.Normal;
            Assert.Equal(new[] { true, false }, calls);
        }
        finally
        {
            hub?.Close();
            ChaosHubWindow.SetCompanionActive = old;
            ConditioningControlPanel.Avalonia.RenderProof.Rendering = oldRender;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task Menu_fall_in_detaches_before_the_posted_run() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var old = ChaosHubWindow.SetCompanionActive;
        var oldRender = ConditioningControlPanel.Avalonia.RenderProof.Rendering;
        var calls = new List<bool>();
        ChaosHubWindow? hub = null;
        try
        {
            ConditioningControlPanel.Avalonia.RenderProof.Rendering = true;
            ChaosHubWindow.SetCompanionActive = calls.Add;
            hub = new ChaosHubWindow();
            hub.Show();
            calls.Clear();
            Click(hub, "BtnMenuFallIn");
            Assert.Equal(new[] { true }, calls);
            Dispatcher.UIThread.RunJobs();
            Assert.True(ChaosRunHost.IsActive);
        }
        finally
        {
            Dispatcher.UIThread.RunJobs();
            ChaosRunHost.ForceShutdown();
            hub?.Close();
            ChaosHubWindow.SetCompanionActive = old;
            ConditioningControlPanel.Avalonia.RenderProof.Rendering = oldRender;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task Fall_in_keeps_the_companion_until_run_cleanup() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var old = ChaosHubWindow.SetCompanionActive;
        var oldRender = ConditioningControlPanel.Avalonia.RenderProof.Rendering;
        var calls = new List<bool>();
        ChaosHubWindow? hub = null;
        try
        {
            ConditioningControlPanel.Avalonia.RenderProof.Rendering = true;
            ChaosHubWindow.SetCompanionActive = calls.Add;
            hub = new ChaosHubWindow();
            hub.Show();
            calls.Clear();
            Click(hub, "BtnMenuDollhouse");
            hub.FallIn();
            Assert.Equal(new[] { true }, calls);
            Dispatcher.UIThread.RunJobs();
            Assert.True(ChaosRunHost.IsActive);
            Assert.Equal(new[] { true, true }, calls);
            ChaosRunHost.ForceShutdown();
            Assert.Equal(new[] { true, true, false }, calls);
        }
        finally
        {
            Dispatcher.UIThread.RunJobs();
            ChaosRunHost.ForceShutdown();
            hub?.Close();
            ChaosHubWindow.SetCompanionActive = old;
            ConditioningControlPanel.Avalonia.RenderProof.Rendering = oldRender;
        }
        return Task.CompletedTask;
    });
}
