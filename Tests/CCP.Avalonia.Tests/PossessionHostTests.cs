using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Possession;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Possession;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class PossessionHostTests
{
    private static void Run(Action<MainShellWindow, StackPanel> body, bool realShell = false) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        settings.Current.Welcomed = settings.Current.HasAcceptedAgeVerification = true;
        settings.Current.WhatMovedCardShown = 1;
        var panel = new StackPanel();
        var shell = new MainShellWindow { Width = 600, Height = 700 };
        if (!realShell) shell.Content = new Grid { Children = { panel } };
        try { shell.Show(); Dispatcher.UIThread.RunJobs(); body(shell, panel); }
        finally { shell.RequestExit(); shell.Close(); CoreSettings.ServiceProvider = null; }
    });
    private static Button Button(string name) => new() { Name = name, Content = "Start", Width = 100, Height = 30 };
    [Fact]
    public void LayersArePassiveIdempotentAndNotTargets() => Run((shell, panel) =>
    {
        Assert.Equal(2, ((Grid)shell.Content!).Children.OfType<Canvas>().Count());
        shell.EnsurePossessionLayers(); shell.EnsurePossessionLayers();
        var layers = ((Grid)shell.Content!).Children.OfType<Canvas>().OrderBy(c => c.ZIndex).ToArray();
        Assert.Equal(2, layers.Length);
        Assert.Same(shell.PossessionRubbleFloor, layers[0]); Assert.Equal(28, layers[0].Height);
        Assert.Equal(8999, layers[0].ZIndex); Assert.Same(shell.PossessionGhostLayer, layers[1]);
        Assert.Equal(9000, layers[1].ZIndex);
        Assert.All(layers, c => { Assert.False(c.IsHitTestVisible); Assert.False(c.ClipToBounds); });
        Assert.DoesNotContain(shell.GetPossessionTargets(), t => t.Visual is Canvas);
    });
    [Fact]
    public void RealShellAndLockdownTagsRespectExitExclusions() => Run((shell, _) =>
    {
        shell.ShowTab("lockdown"); Dispatcher.UIThread.RunJobs();
        Control Find(string name) => global::Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(shell)
            .OfType<Control>().Single(c => c.Name == name);
        Find("LockdownActivePanel").IsVisible = true; shell.UpdateLayout();
        var targets = shell.GetPossessionTargets();
        Assert.Contains(targets, t => t.Key == "DoorHome" && t.Role == PossessionRole.TabHeader && t.DisplayName == "the Home door");
        Assert.Contains(targets, t => t.Key == "TxtLockdownTimer" && t.Role == PossessionRole.Timer && t.DisplayName == "the timer");
        Assert.Contains(targets, t => t.Key == "LockdownCardBorder" && t.Role == PossessionRole.Card);
        Assert.Contains(targets, t => t.Key == "ChkLockdownDose" && t.Role == PossessionRole.Toggle);
        Assert.True(PossessionTree.GetExclude(Find("BtnEmergencyExit")));
        Assert.True(PossessionTree.GetExclude(Find("TxtLockdownExit")));
        Assert.DoesNotContain(targets, t => t.Key is "BtnEmergencyExit" or "TxtLockdownExit");
    }, realShell: true);
    [Fact]
    public void WalkPrunesHiddenExcludedReservedAndSmallButHonorsHandTags() => Run((shell, panel) =>
    {
        var timer = new TextBlock { Name = "TxtLockdownTimer", Text = "00:30" };
        PossessionTree.SetRole(timer, PossessionRole.Timer); PossessionTree.SetName(timer, "the timer");
        var excluded = Button("Excluded"); PossessionTree.SetRole(excluded, PossessionRole.Button);
        PossessionTree.SetExclude(excluded, true);
        panel.Children.Add(Button("BtnEmergencyExit")); panel.Children.Add(timer);
        panel.Children.Add(new StackPanel { IsVisible = false, Children = { Button("Hidden") } });
        panel.Children.Add(excluded); panel.Children.Add(new Button { Name = "Tiny", Width = 2, Height = 2 });
        shell.UpdateLayout();
        var target = Assert.Single(shell.GetPossessionTargets());
        Assert.Same(timer, target.Visual); Assert.Equal(PossessionRole.Timer, target.Role); Assert.Equal("the timer", target.DisplayName);
    });
    [Fact]
    public void LeafRolesAndInnermostCardAndNamesFollowWpf() => Run((shell, panel) =>
    {
        var button = Button("Normal"); button.Content = "  Start\n  here  ";
        var inner = new Border { Background = Brushes.Gray, CornerRadius = new CornerRadius(8), Width = 150, Height = 70, Child = button };
        var outer = new Border { Background = Brushes.Gray, CornerRadius = new CornerRadius(8), Width = 200, Height = 100, Child = inner };
        panel.Children.Add(outer); panel.Children.Add(new CheckBox { Name = "Switch", Width = 100, Height = 30 });
        shell.UpdateLayout(); var targets = shell.GetPossessionTargets();
        Assert.Contains(targets, t => t.Visual == button && t.Role == PossessionRole.Button && t.DisplayName == "the Start here button");
        Assert.Contains(targets, t => t.Visual == inner && t.Role == PossessionRole.Card);
        Assert.DoesNotContain(targets, t => t.Visual == outer || t.Role == PossessionRole.Label);
        Assert.Contains(targets, t => t.Key == "Switch" && t.Role == PossessionRole.Toggle && t.DisplayName == "that toggle");
    });
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void LabelBudgetUsesSizeOrPointerAndKeepsExplicitLabels(int pointer) => Run((shell, panel) =>
    {
        for (int i = 0; i < 30; i++)
            panel.Children.Add(new TextBlock { Name = "Label" + i, Text = "abc", FontSize = 12, Width = 30 + i, Height = 10 });
        var hand = new TextBlock { Name = "Hand", Text = "x", Height = 10 };
        PossessionTree.SetRole(hand, PossessionRole.Label); panel.Children.Add(hand); shell.UpdateLayout();
        if (pointer == 1) shell.MouseMove(new Point(300, 1));
        if (pointer == 2) shell.MouseDown(new Point(300, 1), global::Avalonia.Input.MouseButton.Left);
        var targets = shell.GetPossessionTargets(); Assert.Equal(25, targets.Count);
        Assert.Contains(targets, t => t.Visual == hand);
        Assert.Equal(pointer != 0, targets.Any(t => t.Key == "Label0"));
        Assert.Equal(pointer == 0, targets.Any(t => t.Key == "Label29"));
    });
    [Fact]
    public void CacheFloorsOffThreadReadsAndStableIdentity() => Run((shell, panel) =>
    {
        var now = new DateTime(2026, 1, 1); shell.PossessionNow = () => now;
        panel.Children.Add(Button("First")); shell.UpdateLayout();
        var first = shell.GetPossessionTargets(); first[0].CooldownUntil = now.AddSeconds(45);
        panel.Children.Add(Button("Second")); shell.UpdateLayout(); now = now.AddMilliseconds(500);
        Assert.Same(first, shell.GetPossessionTargets());
        Assert.Same(first, Task.Run(shell.GetPossessionTargets).GetAwaiter().GetResult());
        now = now.AddMilliseconds(251); var next = shell.GetPossessionTargets();
        Assert.Equal(2, next.Count); Assert.Same(first[0], next.Single(t => t.Key == "First"));
        Assert.Equal(first[0].CooldownUntil, next.Single(t => t.Key == "First").CooldownUntil);
        now = now.AddSeconds(11); Assert.NotSame(next, shell.GetPossessionTargets());
    });
    [Fact]
    public void UsabilityAndBoundsFollowWindowStateAndScale() => Run((shell, panel) =>
    {
        var button = Button("Scaled"); panel.Children.Add(new Viewbox { Width = 200, Height = 60, Child = button });
        shell.UpdateLayout(); Assert.True(((IPossessionHost)shell).IsUsable);
        Assert.True(shell.TryWindowBounds(button, out var bounds)); Assert.InRange(bounds.Width, 199, 201);
        var point = shell.PossessionPointOf(button);
        Assert.InRange(Math.Abs(point.X - bounds.X), 0, 0.01); Assert.InRange(Math.Abs(point.Y - bounds.Y), 0, 0.01);
        shell.Hide(); Assert.False(((IPossessionHost)shell).IsUsable);
    });
}
