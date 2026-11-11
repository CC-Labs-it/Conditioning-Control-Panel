using System;
using System.Reflection;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public class TakeoverModLabelsTests
{
    [Fact]
    public Task LabelsFollowModAndLanguageAndUnsubscribeOnDetach() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        if (AvApp.Mods is null) AvApp.StartMods();
        var mods = AvApp.Mods!;
        var originalId = mods.ActiveModId;
        var originalProvider = CoreMods.MakeModAwareProvider;
        var loc = LocalizationManager.Instance;
        var language = loc.CurrentLanguage;
        var view = new BambiTakeoverTabView();
        var count = Count();
        var languageCount = LanguageCount(loc);
        var window = new Window { Content = view };
        try
        {
            loc.SetLanguage("en");
            window.Show(); Dispatcher.UIThread.RunJobs();
            Assert.True(Count() > count);
            Assert.True(LanguageCount(loc) > languageCount);
            var effects = new[] { ("TxtAutoVideo", "Videos"), ("TxtAutoSubliminal", "Subliminals"),
                ("TxtAutoBubbles", "Bubbles"), ("TxtAutoPinkFilter", "Pink Filter"),
                ("TxtAutoLockCards", "Lock Cards"), ("TxtAutoBouncing", "Bouncing"), ("TxtAutoMindwipe", "Mindwipe") };
            mods.ActivateMod(BuiltInMods.SissyHypnoId);
            CoreMods.MakeModAwareProvider = s => s == "Flashes" ? "First effect" : effects.Any(e => e.Item2 == s) ? "First " + s : s;
            CoreMods.RaiseModChanged(null, mods.ActiveMod);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(mods.GetTakeoverLabel(), view.FindControl<TextBlock>("TxtBambiTakeoverHeader")!.Text);
            Assert.Equal("First effect", view.FindControl<TextBlock>("TxtAutoFlash")!.Text);
            foreach (var (name, english) in effects)
                Assert.Equal("First " + english, view.FindControl<TextBlock>(name)!.Text);
            var firstHeading = view.FindControl<TextBlock>("TxtBambiTakeoverHeader")!.Text;
            mods.ActivateMod(BuiltInMods.BambiSleepId);
            CoreMods.MakeModAwareProvider = s => s == "Flashes" ? "Second effect" : s;
            CoreMods.RaiseModChanged(null, mods.ActiveMod);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(mods.GetTakeoverLabel(), view.FindControl<TextBlock>("TxtBambiTakeoverHeader")!.Text);
            Assert.NotEqual(firstHeading, view.FindControl<TextBlock>("TxtBambiTakeoverHeader")!.Text);
            Assert.Equal("Second effect", view.FindControl<TextBlock>("TxtAutoFlash")!.Text);
            loc.SetLanguage("de"); Dispatcher.UIThread.RunJobs();
            Assert.Equal("Second effect", view.FindControl<TextBlock>("TxtAutoFlash")!.Text);
            Assert.Equal(loc.Get("tab_videos"), view.FindControl<TextBlock>("TxtAutoVideo")!.Text);
            Assert.Equal(loc.Get("label_pink_filter"), view.FindControl<TextBlock>("TxtAutoPinkFilter")!.Text);
            window.Content = null;
            Assert.Equal(count, Count());
            Assert.Equal(languageCount, LanguageCount(loc));
        }
        finally
        {
            window.Close();
            mods.ActivateMod(originalId);
            CoreMods.MakeModAwareProvider = originalProvider;
            loc.SetLanguage(language);
            Dispatcher.UIThread.RunJobs();
        }
        return Task.CompletedTask;
    });

    private static int LanguageCount(LocalizationManager loc) => (typeof(LocalizationManager).GetField("LanguageChanged", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(loc) as Delegate)?.GetInvocationList().Length ?? 0;

    private static int Count() => (typeof(CoreMods).GetField(nameof(CoreMods.ModChanged), BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null) as Delegate)?.GetInvocationList().Length ?? 0;
}
