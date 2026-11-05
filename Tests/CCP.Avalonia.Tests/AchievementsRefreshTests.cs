using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Skia;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class AchievementsRefreshTests
{
    [Fact]
    public async Task ModAndLanguageRefreshVisibleTilesAndDetach()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var dir = Directory.CreateTempSubdirectory("ccp-ach-refresh-");
            var oldArt = CoreModArt.OverridePathProvider;
            var oldName = CoreMods.MakeModAwareProvider;
            var language = LocalizationManager.Instance.CurrentLanguage;
            Window? host = null;
            try
            {
                var png = Path.Combine(dir.FullName, "badge.png");
                using (var bitmap = new global::Avalonia.Media.Imaging.WriteableBitmap(new PixelSize(6, 3), new Vector(96, 96))) bitmap.Save(png);
                var engine = new AchievementEngine(new AchievementStore(Path.Combine(dir.FullName, "achievements.json")));
                engine.TryUnlock("plastic_initiation");
                var view = new AchievementsTabView(engine);
                host = new Window { Content = view };
                host.Show();
                Dispatcher.UIThread.RunJobs();
                var card = view.Cards.Single(c => (string)c.Tag! == "plastic_initiation");
                Image Badge() => card.GetVisualDescendants().OfType<Image>().First();
                string[] Texts() => card.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").ToArray();
                var original = Badge().Source;
                CoreModArt.OverridePathProvider = p => p.StartsWith("achievements/", StringComparison.Ordinal) ? png : null;
                CoreMods.MakeModAwareProvider = s => "MOD " + s;
                CoreMods.RaiseModChanged(null, new ModPackage(new ModManifest(), null, false));
                Dispatcher.UIThread.RunJobs();
                Assert.NotSame(original, Badge().Source);
                Assert.Contains("MOD " + Loc.Get("achievement_plastic_initiation_name"), Texts());
                LocalizationManager.Instance.SetLanguage("fr");
                Dispatcher.UIThread.RunJobs();
                Assert.Contains("MOD " + Loc.Get("achievement_plastic_initiation_name"), Texts());
                view.IsVisible = false;
                var applies = view.TileApplies;
                CoreModArt.OverridePathProvider = _ => null;
                CoreMods.MakeModAwareProvider = s => s;
                CoreMods.RaiseModChanged(null, new ModPackage(new ModManifest(), null, false));
                LocalizationManager.Instance.SetLanguage("en");
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(applies, view.TileApplies);
                view.IsVisible = true;
                Assert.Same(original, Badge().Source);
                Assert.Contains(Loc.Get("achievement_plastic_initiation_name"), Texts());
                var modEvent = typeof(CoreMods).GetField("ModChanged", BindingFlags.Static | BindingFlags.NonPublic)!;
                var langEvent = typeof(LocalizationManager).GetField("LanguageChanged", BindingFlags.Instance | BindingFlags.NonPublic)!;
                int Subscribers(FieldInfo field, object? target) => ((Delegate?)field.GetValue(target))?.GetInvocationList().Count(d => d.Target == view) ?? 0;
                Assert.Equal(1, Subscribers(modEvent, null));
                Assert.Equal(1, Subscribers(langEvent, LocalizationManager.Instance));
                host.Content = null;
                Assert.Equal(0, Subscribers(modEvent, null));
                Assert.Equal(0, Subscribers(langEvent, LocalizationManager.Instance));
                applies = view.TileApplies;
                CoreMods.RaiseModChanged(null, new ModPackage(new ModManifest(), null, false));
                LocalizationManager.Instance.SetLanguage("fr");
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(applies, view.TileApplies);
            }
            finally
            {
                host?.Close();
                CoreModArt.OverridePathProvider = oldArt;
                CoreMods.MakeModAwareProvider = oldName;
                LocalizationManager.Instance.SetLanguage(language);
                dir.Delete(true);
            }
            return Task.CompletedTask;
        });
    }
}
