using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

[Collection(RunsAloneCollection.Name)]
public sealed class EnhancementPurchaseTests
{
    [Fact]
    public Task CardsConfirmGuardRequestsShowErrorsAndRefresh() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        AmbientBeatLoopsTests.Setup();
        var old = CoreSettings.ServiceProvider;
        var settings = new SettingsService(); CoreSettings.ServiceProvider = () => settings;
        var s = settings.Current; s.SkillPoints = 100; s.NightTimeUsageCount = 10; s.UnifiedId = "purchase-test"; s.UnlockedSkills = new();
        Window? host = null; Window? secretHost = null;
        try
        {
            var tab = new EnhancementsTabView();
            host = new Window { Width = 1400, Height = 700, Content = tab }; host.Show(); Dispatcher.UIThread.RunJobs();
            int asked = 0, sent = 0, errors = 0; bool confirm = false;
            tab.AskPurchase = (_, _, _) => { asked++; return Task.FromResult(confirm); };
            tab.PurchaseError = (_, _, _) => { errors++; return Task.CompletedTask; };
            var pending = new TaskCompletionSource<(bool, string?, int?, List<string>?, long?)>();
            tab.PurchaseForTest = (_, _) => { sent++; return pending.Task; };
            var normal = tab.FindControl<Canvas>("SkillTreeCanvas")!.Children.OfType<Control>().Single(c => Equals(c.Tag, "pink_hours"));
            var point = normal.TranslatePoint(new Point(40, 40), host)!.Value;
            host.MouseDown(point, MouseButton.Left); host.MouseUp(point, MouseButton.Left); Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, asked); Assert.Equal(0, sent);
            normal = tab.FindControl<Canvas>("SkillTreeCanvas")!.Children.OfType<Control>().Single(c => Equals(c.Tag, "pink_hours")); point = normal.TranslatePoint(new Point(40, 40), host)!.Value;
            confirm = true;
            host.MouseDown(point, MouseButton.Left); host.MouseUp(point, MouseButton.Left); Dispatcher.UIThread.RunJobs();
            host.MouseDown(point, MouseButton.Left); host.MouseUp(point, MouseButton.Left); Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, asked); Assert.Equal(1, sent); Assert.False(normal.IsEnabled);
            pending.SetResult((false, "denied", null, null, null)); await Task.Yield(); Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, errors); Assert.Equal(100, s.SkillPoints); Assert.Empty(s.UnlockedSkills);
            normal = tab.FindControl<Canvas>("SkillTreeCanvas")!.Children.OfType<Control>().Single(c => Equals(c.Tag, "pink_hours"));
            point = normal.TranslatePoint(new Point(40, 40), host)!.Value;
            tab.PurchaseForTest = (_, _) => { sent++; return Task.FromResult((true, (string?)null, (int?)98, (List<string>?)new List<string> { "pink_hours" }, (long?)2)); };
            host.MouseDown(point, MouseButton.Left); host.MouseUp(point, MouseButton.Left); Dispatcher.UIThread.RunJobs();
            Assert.Contains("pink_hours", s.UnlockedSkills); Assert.Equal(98, s.SkillPoints);
            var refreshed = tab.FindControl<Canvas>("SkillTreeCanvas")!.Children.OfType<Control>().Single(c => Equals(c.Tag, "pink_hours"));
            Assert.NotSame(normal, refreshed);
            var secret = tab.SecretSkillCard(SkillDefinition.All.Single(x => x.Id == "night_shift"));
            secretHost = new Window { Width = 250, Height = 120, Content = secret }; secretHost.Show(); Dispatcher.UIThread.RunJobs();
            tab.PurchaseForTest = (_, _) => { sent++; return Task.FromResult((true, (string?)null, (int?)88, (List<string>?)new List<string> { "night_shift" }, (long?)12)); };
            var secretPoint = secret.TranslatePoint(new Point(20, 20), secretHost)!.Value;
            secretHost.MouseDown(secretPoint, MouseButton.Left); secretHost.MouseUp(secretPoint, MouseButton.Left); Dispatcher.UIThread.RunJobs();
            Assert.Contains("night_shift", s.UnlockedSkills); Assert.Contains("pink_hours", s.UnlockedSkills); Assert.Equal(88, s.SkillPoints);
        }
        finally { secretHost?.Close(); host?.Close(); CoreSettings.ServiceProvider = old; }
    });
}
