using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public class AwarenessPrivacyRingTests
{
    [Fact]
    public void ForegroundSamplingIsThrottledOnlyWhenScopingIsOff()
    {
        var now = new DateTime(2026, 10, 31, 12, 0, 0);
        var engine = new KeywordAudioRuntime { Now = () => now };
        Assert.True(engine.NeedsForegroundSample(false));
        now = now.AddMilliseconds(1999);
        Assert.False(engine.NeedsForegroundSample(false));
        Assert.True(engine.NeedsForegroundSample(true));
        now = now.AddMilliseconds(1);
        Assert.True(engine.NeedsForegroundSample(false));
    }

    [Fact]
    public Task RingIsBoundedRecentDistinctAndIgnoresOwnUnknownAndInactiveInput() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        CoreSettings.Current.KeywordTriggersEnabled = true;
        CoreSettings.Current.KeywordTriggerAppScope = AwarenessAppScope.OnlyListed;
        CoreSettings.Current.KeywordTriggerApps = new(); // suppressed input still supplies suggestions
        CoreEntitlement.HasPremiumProvider = () => true;
        var engine = new KeywordAudioRuntime();
        engine.Character('x', "inactive");
        engine.Start();
        for (var i = 0; i < 10; i++) engine.Character('x', $"app{i}");
        engine.Character('x', "APP2");
        engine.Character('x', "self", own: true);
        engine.Character('x', " ");
        engine.Character('x');
        CoreSettings.Current.KeywordTriggersEnabled = false;
        engine.Character('x', "disabled");
        CoreSettings.Current.KeywordTriggersEnabled = true;
        CoreEntitlement.HasPremiumProvider = () => false;
        CoreEntitlement.IsFreeTodayProvider = _ => false;
        engine.Character('x', "unentitled");
        engine.Stop();
        engine.Character('x', "stopped");
        Assert.Equal(new[] { "app2", "app9", "app8", "app7", "app6", "app5", "app4", "app3" }, engine.GetRecentForegroundApps());
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task RecentAppCanBePickedAndHiddenAfterItLeavesForeground(bool allow) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var engine = KeywordTriggerHead.Engine;
        // Restore the live service's session-only collection as well as the suite's settings snapshot.
        var ring = (LinkedList<string>)typeof(KeywordAudioRuntime).GetField("_seenApps", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(engine)!;
        var saved = ring.ToArray();
        var active = engine.IsActive;
        MainShellWindow? shell = null;
        try
        {
            ring.Clear();
            CoreSettings.Current.KeywordTriggersEnabled = true;
            CoreSettings.Current.KeywordTriggerAppScope = AwarenessAppScope.Everywhere;
            CoreSettings.Current.KeywordTriggerIgnoreOwnFocus = false;
            CoreSettings.Current.KeywordTriggers = new();
            CoreSettings.Current.AwarenessDenySeeded = true;
            CoreSettings.Current.AwarenessDenyList = new() { "blocked" };
            CoreSettings.Current.AwarenessTitleAllowList = new();
            CoreEntitlement.HasPremiumProvider = () => true;
            engine.Start();
            engine.Character('x', "recent-app");
            engine.Character('x', "blocked");
            shell = new MainShellWindow();
            shell.Show();
            var vm = new AwarenessPrivacyViewModel(() => shell);
            Assert.Contains(vm.SeenApps, chip => chip.Label == "recent-app");
            Assert.DoesNotContain(vm.SeenApps, chip => chip.Label == "blocked");
            if (allow) vm.AllowPageTitles = true;
            else vm.AddDenyCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            var picker = Assert.Single(shell.OwnedWindows.OfType<AwarenessAppPickerDialog>());
            var rows = picker.FindControl<ItemsControl>("ItemList")!.ItemsSource!.Cast<AwarenessPickRow>();
            var row = Assert.Single(rows, r => r.Raw == "recent-app");
            Assert.False(row.IsListed);
            row.IsListed = true;
            picker.FindControl<Button>("BtnSave")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (var i = 0; i < 100 && !(allow ? CoreSettings.Current.AwarenessTitleAllowList : CoreSettings.Current.AwarenessDenyList).Contains("recent-app"); i++)
            { Dispatcher.UIThread.RunJobs(); await Task.Yield(); }
            Assert.Contains("recent-app", allow ? CoreSettings.Current.AwarenessTitleAllowList : CoreSettings.Current.AwarenessDenyList);
            if (allow)
            {
                vm.Sync();
                var chip = Assert.Single(vm.SeenApps, c => c.Label == "recent-app");
                chip.ActionCommand.Execute(null);
            }
            vm.Sync();
            Assert.Contains("recent-app", CoreSettings.Current.AwarenessDenyList);
            Assert.DoesNotContain(vm.SeenApps, c => c.Label == "recent-app");
        }
        finally
        {
            shell?.Close();
            if (!active) engine.Stop();
            ring.Clear();
            foreach (var app in saved) ring.AddLast(app);
        }
    });
}
