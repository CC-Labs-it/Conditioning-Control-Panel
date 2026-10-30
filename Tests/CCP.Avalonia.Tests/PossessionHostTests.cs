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
    private static void Run(Action<MainShellWindow, StackPanel> body) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>().WithInterFont().With(ConditioningControlPanel.Avalonia.Platform.AppFonts.Options()).ConfigureFonts(ConditioningControlPanel.Avalonia.Platform.AppFonts.Register).UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        settings.Current.Welcomed = true; settings.Current.HasAcceptedAgeVerification = true; settings.Current.WhatMovedCardShown = 1;
        var panel = new StackPanel();
        var shell = new MainShellWindow { Width = 600, Height = 700, Content = new Grid { Children = { panel } } };
        try
        {
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            body(shell, panel);
        }
        finally
        {
            shell.RequestExit();
            shell.Close();
            CoreSettings.ServiceProvider = null;
        }
    });
    private static Button Button(string name) => new() { Name = name, Content = "Start", Width = 100, Height = 30 };
    private static void Layout(MainShellWindow shell) => shell.UpdateLayout();
    [Fact]
    public void LayersAreIdempotentPassiveAndNeverTargets() => Run((shell, panel) =>
    {
        panel.Children.Add(Button("Normal"));
        Layout(shell);
        shell.EnsurePossessionLayers();
        shell.EnsurePossessionLayers();
        var layers = ((Grid)shell.Content!).Children.OfType<Canvas>().OrderBy(c => c.ZIndex).ToArray();
        Assert.Equal(2, layers.Length);
        Assert.Same(layers[0], shell.PossessionRubbleFloor);
        Assert.Same(layers[1], shell.PossessionGhostLayer);
        Assert.Equal(8999, layers[0].ZIndex);
        Assert.Equal(28, layers[0].Height);
        Assert.Equal(9000, layers[1].ZIndex);
        Assert.All(layers, c => { Assert.False(c.IsHitTestVisible); Assert.False(c.ClipToBounds); });
        Assert.DoesNotContain(shell.GetPossessionTargets(), t => t.Visual is Canvas);
    });
    [Fact]
    public void LeafRolesPruneTemplatesAndInnermostCardWins() => Run((shell, panel) =>
    {
        var button = Button("Normal");
        var inner = new Border { Background = Brushes.Gray, CornerRadius = new CornerRadius(8),
            Width = 150, Height = 70, Child = button };
        var outer = new Border { Background = Brushes.Gray, CornerRadius = new CornerRadius(8),
            Width = 200, Height = 100, Child = inner };
        panel.Children.Add(outer);
        panel.Children.Add(new CheckBox { Name = "Switch", Content = "Check", Width = 100, Height = 30 });
        Layout(shell);
        var targets = shell.GetPossessionTargets();
        Assert.Contains(targets, t => t.Visual == button && t.Role == PossessionRole.Button && t.DisplayName == "the Start button");
        Assert.Contains(targets, t => t.Visual == inner && t.Role == PossessionRole.Card);
        Assert.DoesNotContain(targets, t => t.Visual == outer);
        Assert.Contains(targets, t => t.Key == "Switch" && t.Role == PossessionRole.Toggle);
        Assert.DoesNotContain(targets, t => t.Role == PossessionRole.Label);
    });
    [Fact]
    public void RemainingLeafTypesKeepTheirWpfRoles() => Run((shell, panel) =>
    {
        Control[] controls = { new Slider(), new ComboBox(), new TextBox(), new ProgressBar(),
            new Image { Source = new global::Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(100, 50)) }, new TextBlock { Text = "Heading", FontSize = 20 } };
        PossessionRole[] roles = { PossessionRole.Slider, PossessionRole.Combo, PossessionRole.TextBox,
            PossessionRole.Progress, PossessionRole.Image, PossessionRole.Title };
        foreach (var c in controls) { c.Width = 100; c.Height = 50; panel.Children.Add(c); }
        Layout(shell);
        for (int i = 0; i < controls.Length; i++)
            Assert.Contains(shell.GetPossessionTargets(), t => t.Visual == controls[i] && t.Role == roles[i]);
    });
    [Fact]
    public void ExclusionVisibilityAndHandTagsFollowWpfPrecedence() => Run((shell, panel) =>
    {
        var reserved = Button("BtnEmergencyExit");
        var timer = new TextBlock { Name = "TxtLockdownTimer", Text = "00:30" };
        PossessionTree.SetRole(timer, PossessionRole.Timer);
        PossessionTree.SetName(timer, "the timer");
        var excluded = Button("Excluded");
        PossessionTree.SetRole(excluded, PossessionRole.Button);
        PossessionTree.SetExclude(excluded, true);
        panel.Children.Add(reserved);
        panel.Children.Add(new StackPanel { IsHitTestVisible = false, Children = { timer } });
        panel.Children.Add(new StackPanel { IsVisible = false, Children = { Button("Hidden") } });
        panel.Children.Add(new StackPanel { Children = { excluded } });
        panel.Children.Add(new Button { Name = "Tiny", Width = 2, Height = 2 });
        Layout(shell);
        var target = Assert.Single(shell.GetPossessionTargets());
        Assert.Same(timer, target.Visual);
        Assert.Equal(PossessionRole.Timer, target.Role);
        Assert.Equal("the timer", target.DisplayName);
    });
    [Fact]
    public void NonHitTestableAncestorsKeepAutomaticTargetsOut() => Run((shell, panel) =>
    {
        var button = Button("PassiveChild");
        panel.Children.Add(new StackPanel { IsHitTestVisible = false, Children = { button } });
        Layout(shell);
        Assert.True(button.IsHitTestVisible);
        Assert.DoesNotContain(shell.GetPossessionTargets(), t => t.Visual == button);
    });
    [Theory, InlineData(false), InlineData(true)]
    public void EffectivelyDisabledControlsAreNotTargets(bool command) => Run((shell, panel) =>
    {
        var button = Button("Disabled");
        if (command) button.Command = new ConditioningControlPanel.Avalonia.Views.Controls.Companion.RelayCommand(() => throw new InvalidOperationException(), () => false);
        panel.Children.Add(new StackPanel { IsEnabled = command, Children = { button } });
        Layout(shell);
        Assert.True(button.IsEnabled);
        Assert.False(button.IsEffectivelyEnabled);
        Assert.DoesNotContain(shell.GetPossessionTargets(), t => t.Visual == button);
    });
    [Fact]
    public void LabelBudgetKeepsLargestAndExplicitLabelsDoNotSpendIt() => Run((shell, panel) =>
    {
        for (int i = 0; i < 30; i++)
            panel.Children.Add(new TextBlock { Name = "Label" + i, Text = "abc", FontSize = 12,
                Width = 30 + i, Height = 10 });
        var hand = new TextBlock { Name = "Hand", Text = "x", Height = 10 };
        PossessionTree.SetRole(hand, PossessionRole.Label);
        panel.Children.Add(hand);
        Layout(shell);
        var targets = shell.GetPossessionTargets();
        Assert.Equal(25, targets.Count);
        Assert.Contains(targets, t => t.Visual == hand);
        Assert.DoesNotContain(targets, t => t.Key == "Label0");
        Assert.Contains(targets, t => t.Key == "Label29");
    });
    [Fact]
    public void PointerKeepsNearbyLabelsAndNamesUseWpfFallbacks() => Run((shell, panel) =>
    {
        for (int i = 0; i < 30; i++)
            panel.Children.Add(new TextBlock { Name = "Label" + i, Text = "abc", FontSize = 12, Width = 50, Height = 10 });
        var named = Button("Named");
        named.Content = "  Start\n  here  ";
        var fallback = Button("Fallback");
        fallback.Content = new object();
        panel.Children.Add(named);
        panel.Children.Add(fallback);
        Layout(shell);
        shell.MouseMove(new Point(300, 1));
        var targets = shell.GetPossessionTargets();
        Assert.Contains(targets, t => t.Key == "Label0");
        Assert.DoesNotContain(targets, t => t.Key == "Label29");
        Assert.Contains(targets, t => t.Key == "Named" && t.DisplayName == "the Start here button");
        Assert.Contains(targets, t => t.Key == "Fallback" && t.DisplayName == "that button");
    });
    [Fact]
    public void CacheFloorsAndOffThreadReadsPreserveTargetIdentity() => Run((shell, panel) =>
    {
        var now = new DateTime(2026, 1, 1);
        shell.PossessionNow = () => now;
        panel.Children.Add(Button("First"));
        Layout(shell);
        var first = shell.GetPossessionTargets();
        first[0].CooldownUntil = now.AddSeconds(45);
        panel.Children.Add(Button("Second"));
        Layout(shell);
        now = now.AddMilliseconds(500);
        Assert.Same(first, shell.GetPossessionTargets());
        Assert.Same(first, Task.Run(shell.GetPossessionTargets).GetAwaiter().GetResult());
        now = now.AddMilliseconds(251);
        var next = shell.GetPossessionTargets();
        Assert.Equal(2, next.Count);
        Assert.Same(first[0], next.Single(t => t.Key == "First"));
        Assert.Equal(first[0].CooldownUntil, next.Single(t => t.Key == "First").CooldownUntil);
        now = now.AddSeconds(11);
        Assert.NotSame(next, shell.GetPossessionTargets());
    });
    [Fact]
    public void UsabilityAndBoundsFollowWindowStateAndScale() => Run((shell, panel) =>
    {
        var button = Button("Scaled");
        panel.Children.Add(new Viewbox { Width = 200, Height = 60, Child = button });
        Layout(shell);
        Assert.Empty(shell.OwnedWindows.Where(w => w.IsVisible).Select(w => w.GetType().Name));
        Assert.True(((IPossessionHost)shell).IsUsable);
        Assert.True(shell.TryWindowBounds(button, out var bounds));
        Assert.InRange(bounds.Width, 199, 201);
        var point = shell.PossessionPointOf(button);
        Assert.InRange(Math.Abs(point.X - bounds.X), 0, 0.01);
        Assert.InRange(Math.Abs(point.Y - bounds.Y), 0, 0.01);
        var owned = new Window();
        owned.Show(shell);
        Assert.False(((IPossessionHost)shell).IsUsable);
        owned.Close();
        shell.Hide();
        Assert.False(((IPossessionHost)shell).IsUsable);
        shell.RequestExit();
        shell.Close();
        Assert.Empty(shell.GetPossessionTargets());
    });
}
