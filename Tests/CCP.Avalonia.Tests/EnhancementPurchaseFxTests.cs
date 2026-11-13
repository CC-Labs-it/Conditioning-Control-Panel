using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class EnhancementPurchaseFxTests
{
    private sealed class Clock : TimeProvider
    {
        public long T;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => T;
        public void Advance(double seconds) => T += (long)(seconds * TimeSpan.TicksPerSecond);
    }
    private sealed class Reply : HttpMessageHandler
    {
        public bool Success;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Success
                ? "{\"success\":true,\"skill_points\":992,\"unlocked_skills\":[\"pink_hours\",\"sparkle_boost_1\"]}"
                : "{\"success\":false,\"error\":\"no\"}") });
    }
    private static Control Node(EnhancementsTabView tab) => tab.FindControl<Canvas>("SkillTreeCanvas")!.Children
        .OfType<Control>().Single(c => (string?)c.Tag == "sparkle_boost_1");

    [Fact]
    public void SuccessfulPurchaseBurstsBeforeRepaintAndFailuresDoNot() => ShellEventFxTests.WithShell((shell, engine, _) =>
    {
        var s = CoreSettings.Current;
        s.SkillPoints = 1000; s.UnifiedId = "u_test"; s.UnlockedSkills = new List<string> { "pink_hours" };
        shell.ShowTab("enhancements"); shell.Activate(); Dispatcher.UIThread.RunJobs(); shell.UpdateLayout();
        var tab = shell.Named<EnhancementsTabView>("EnhancementsTab")!;
        var reply = new Reply();
        tab.PurchaseService = new SkillPurchase(() => s, reply) { Save = () => { } };
        bool confirm = false;
        tab.AskPurchase = (_, _, _) => Task.FromResult(confirm);
        tab.TellFailure = (_, _, _) => Task.CompletedTask;
        var node = Node(tab);
        tab.PurchaseAsync(node, "sparkle_boost_1").GetAwaiter().GetResult();
        Assert.Equal(0, ShellEventFxTests.Bursts(shell));
        confirm = true;
        tab.PurchaseAsync(node, "sparkle_boost_1").GetAwaiter().GetResult();
        Assert.Equal(0, ShellEventFxTests.Bursts(shell));
        node = Node(tab); shell.UpdateLayout();
        var host = OverlayLayer.GetOverlayLayer(node)!;
        var point = node.TranslatePoint(new Point(node.Bounds.Width / 2, node.Bounds.Height / 2), host)!.Value;
        reply.Success = true;
        tab.PurchaseAsync(node, "sparkle_boost_1").GetAwaiter().GetResult();
        Assert.Equal(1, ShellEventFxTests.Bursts(shell));
        var layer = ShellEventFxTests.Layer(shell);
        Assert.Equal(point.X - 280, Canvas.GetLeft(layer), 3);
        Assert.Equal(point.Y - 280, Canvas.GetTop(layer), 3);
        Assert.NotSame(node, Node(tab));
    });

    [Fact]
    public void CrossingRankBurstsPrestigeRowAndSweepsOnce() => ShellEventFxTests.WithShell((shell, engine, _) =>
    {
        var previous = BeatLoop.Time;
        var clock = new Clock(); BeatLoop.Time = clock;
        try
        {
            var s = CoreSettings.Current;
            s.SkillPoints = 1000; s.UnifiedId = "u_test"; s.UnlockedSkills = new List<string> { "pink_hours" };
            engine.Progress.LifetimeSkillPointsSpent = 95;
            shell.ShowTab("enhancements"); shell.Activate(); Dispatcher.UIThread.RunJobs(); shell.UpdateLayout();
            var tab = shell.Named<EnhancementsTabView>("EnhancementsTab")!;
            var row = tab.PrestigeRow!;
            var host = OverlayLayer.GetOverlayLayer(row)!;
            var at = row.TranslatePoint(new Point(row.Bounds.Width / 2, row.Bounds.Height / 2), host)!.Value;
            tab.PurchaseService = new SkillPurchase(() => s, new Reply { Success = true })
            { Save = () => { }, PointsSpent = _ => engine.Progress.LifetimeSkillPointsSpent = 105 };
            tab.AskPurchase = (_, _, _) => Task.FromResult(true);
            tab.PurchaseAsync(Node(tab), "sparkle_boost_1").GetAwaiter().GetResult();
            Assert.Equal(105, engine.Progress.LifetimeSkillPointsSpent);
            Assert.Equal(2, ShellEventFxTests.Bursts(shell));
            var layer = ShellEventFxTests.Layer(shell);
            Assert.Equal(at.X - 280, Canvas.GetLeft(layer), 3);
            Assert.Equal(at.Y - 280, Canvas.GetTop(layer), 3);
            var sheen = shell.Named<Border>("PrestigeSheen")!;
            clock.Advance(.3); BeatLoop.StepAll(); Assert.Equal(.16, sheen.Opacity, 3);
            clock.Advance(.85); BeatLoop.StepAll(); Assert.Equal(0, sheen.Opacity, 6);
            Assert.DoesNotContain(BeatLoop.Running, loop => loop.Owner == sheen);
        }
        finally { BeatLoop.Time = previous; }
    });

    [Fact]
    public void HiddenNodeFallsBackToEnhancementsNavigation() => ShellEventFxTests.WithShell((shell, engine, _) =>
    {
        shell.ShowTab("enhancements"); shell.Activate(); Dispatcher.UIThread.RunJobs(); shell.UpdateLayout();
        var tab = shell.Named<EnhancementsTabView>("EnhancementsTab")!;
        var node = Node(tab);
        node.IsVisible = false; Dispatcher.UIThread.RunJobs(); shell.UpdateLayout();
        var fallback = shell.NavAnchorForTab("enhancements")!;
        var host = OverlayLayer.GetOverlayLayer(fallback)!;
        var at = fallback.TranslatePoint(new Point(fallback.Bounds.Width / 2, fallback.Bounds.Height / 2), host)!.Value;
        shell.CelebrateEnhancementPurchase(node, tab.PrestigeRow, 1);
        Assert.Equal(1, ShellEventFxTests.Bursts(shell));
        var layer = ShellEventFxTests.Layer(shell);
        Assert.Equal(at.X - 280, Canvas.GetLeft(layer), 3);
        Assert.Equal(at.Y - 280, Canvas.GetTop(layer), 3);
    });

    [Fact]
    public void MotionOffAndInactiveWindowRefuseBothEffects() => ShellEventFxTests.WithShell((shell, engine, _) =>
    {
        shell.ShowTab("enhancements"); shell.Activate(); Dispatcher.UIThread.RunJobs(); shell.UpdateLayout();
        var tab = shell.Named<EnhancementsTabView>("EnhancementsTab")!;
        engine.Progress.LifetimeSkillPointsSpent = 105;
        CoreSettings.Current.MotionLevel = MotionLevel.Off;
        shell.CelebrateEnhancementPurchase(Node(tab), tab.PrestigeRow, 1);
        Assert.Equal(0, ShellEventFxTests.Bursts(shell));
        Assert.Equal(0, shell.Named<Border>("PrestigeSheen")!.Opacity);
        Assert.DoesNotContain(BeatLoop.Running, loop => loop.Owner == shell.Named<Border>("PrestigeSheen"));
        CoreSettings.Current.MotionLevel = MotionLevel.Full;
        typeof(WindowBase).GetMethod("HandleDeactivated", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(shell, null);
        Assert.False(shell.IsActive);
        shell.CelebrateEnhancementPurchase(Node(tab), tab.PrestigeRow, 1);
        Assert.Equal(0, ShellEventFxTests.Bursts(shell));
        Assert.Equal(0, shell.Named<Border>("PrestigeSheen")!.Opacity);
        Assert.DoesNotContain(BeatLoop.Running, loop => loop.Owner == shell.Named<Border>("PrestigeSheen"));
    });
}
