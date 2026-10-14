using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;
using Newtonsoft.Json;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class QuestsTabArtTests
{
    [Fact]
    public void DailyAndWeeklyArtReuseTheDecodeAndReloadOnModSwitch() => Run((tab, quests, _, root, host) =>
    {
        var image = Path.Combine(root, "mod.png");
        WritePng(image, 13, 9);
        CoreModArt.OverridePathProvider = p => p.StartsWith("quests/", StringComparison.Ordinal) ? image : null;
        host.Show();
        Dispatcher.UIThread.RunJobs();
        var daily = tab.FindControl<DailyQuestCard>("DailyCard0")!.FindControl<Image>("Art")!;
        var weekly = tab.FindControl<Image>("ImgWeeklyQuest")!;
        var first = Assert.IsType<Bitmap>(daily.Source);
        Assert.Equal(new PixelSize(13, 9), first.PixelSize);
        Assert.Same(first, weekly.Source);

        quests.TrackBubblesPopped(1);
        Dispatcher.UIThread.RunJobs();
        Assert.Same(first, daily.Source);
        WritePng(image, 27, 17);
        CoreMods.RaiseModChanged(null, new ModPackage(new ModManifest(), null, false));
        Dispatcher.UIThread.RunJobs();
        Assert.NotSame(first, daily.Source);
        Assert.Equal(new PixelSize(27, 17), Assert.IsType<Bitmap>(daily.Source).PixelSize);
        Assert.Same(daily.Source, weekly.Source);
    });

    [Fact]
    public void DownloadedQuestImageWinsOverTheModOverride() => Run((tab, _, _, root, host) =>
    {
        var def = QuestDefinition.DailyQuests.Find(d => d.Id == "pop_parade_d")!;
        var previous = def.CachedImagePath;
        try
        {
            var cached = Path.Combine(root, "cached.png");
            var mod = Path.Combine(root, "override.png");
            WritePng(cached, 11, 7);
            WritePng(mod, 19, 13);
            def.CachedImagePath = cached;
            CoreModArt.OverridePathProvider = _ => mod;
            host.Show();
            Dispatcher.UIThread.RunJobs();
            var art = tab.FindControl<DailyQuestCard>("DailyCard0")!.FindControl<Image>("Art")!;
            Assert.Equal(new PixelSize(11, 7), Assert.IsType<Bitmap>(art.Source).PixelSize);
            def.CachedImagePath = Path.Combine(root, "not-downloaded.png");
            tab.RefreshQuestUI();
            Assert.Equal(new PixelSize(19, 13), Assert.IsType<Bitmap>(art.Source).PixelSize);
        }
        finally { def.CachedImagePath = previous; }
    });

    [Fact]
    public void ChargeBalanceRepaintsWhileAttachedAndUnsubscribesWhenDetached() => Run((tab, _, settings, _, host) =>
    {
        host.Show();
        Dispatcher.UIThread.RunJobs();
        settings.StreakFixCharges = 7;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("7", tab.FindControl<TextBlock>("TxtStreakFixCharges")!.Text);
        host.Content = null;
        settings.StreakFixCharges = 9;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("7", tab.FindControl<TextBlock>("TxtStreakFixCharges")!.Text);
    });

    [Fact]
    public void AFailedDecodeLeavesTheQuestUsableAndIsRetriedAfterTheFileArrives() => Run((tab, _, _, root, host) =>
    {
        var image = Path.Combine(root, "not-ready.png");
        File.WriteAllText(image, "not a picture");
        CoreModArt.OverridePathProvider = _ => image;
        host.Show();
        Dispatcher.UIThread.RunJobs();
        var card = tab.FindControl<DailyQuestCard>("DailyCard0")!;
        Assert.Null(card.FindControl<Image>("Art")!.Source);
        Assert.NotEmpty(card.FindControl<TextBlock>("TxtName")!.Text!);
        Assert.True(card.FindControl<Button>("BtnReroll")!.IsEnabled);
        WritePng(image, 17, 11);
        tab.RefreshQuestUI();
        Assert.Equal(new PixelSize(17, 11), Assert.IsType<Bitmap>(card.FindControl<Image>("Art")!.Source).PixelSize);
    });

    [Fact]
    public void OriginalModOverrideWinsBeforeTheModeSwap() => Run((tab, _, settings, root, host) =>
    {
        var def = QuestDefinition.DailyQuests.Find(d => d.Id == "pop_parade_d")!;
        var previous = (def.ImagePath, def.CachedImagePath);
        try
        {
            def.ImagePath = QuestDefinition.PackResources + "logo.png";
            def.CachedImagePath = null;
            settings.ActiveModId = BuiltInMods.SissyHypnoId;
            CoreMods.ActiveModIdProvider = () => settings.ActiveModId;
            var image = Path.Combine(root, "logo-override.png");
            WritePng(image, 23, 15);
            CoreModArt.OverridePathProvider = p => p == "logo.png" ? image : null;
            host.Show();
            Dispatcher.UIThread.RunJobs();
            var art = tab.FindControl<DailyQuestCard>("DailyCard0")!.FindControl<Image>("Art")!;
            Assert.Equal(new PixelSize(23, 15), Assert.IsType<Bitmap>(art.Source).PixelSize);
            CoreModArt.OverridePathProvider = null;
            CoreMods.RaiseModChanged(null, new ModPackage(new ModManifest(), null, false));
            Dispatcher.UIThread.RunJobs();
            using var stream = AssetLoader.Open(new Uri("avares://CCP.Avalonia/Resources/logo2.png"));
            using var expected = new Bitmap(stream);
            Assert.Equal(expected.PixelSize, Assert.IsType<Bitmap>(art.Source).PixelSize);
        }
        finally { (def.ImagePath, def.CachedImagePath) = previous; }
    });

    [Fact]
    public Task ServerSeasonTitleRefreshesLiveAndStopsFollowingAfterDetach() => RunAsync(async (tab, _, _, root, host) =>
    {
        var handler = new DefinitionsHandler();
        using var definitions = new QuestDefinitionService(root, new HttpClient(handler));
        AvApp.QuestDefinitions = definitions;
        host.Show();
        await definitions.RefreshFromServerAsync();
        Dispatcher.UIThread.RunJobs();
        var title = tab.FindControl<TextBlock>("TxtSeasonTitle")!;
        Assert.Equal("First season", title.Text);
        handler.Title = "Second season";
        await definitions.RefreshFromServerAsync();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Second season", title.Text);
        host.Content = null;
        handler.Title = "Detached season";
        await definitions.RefreshFromServerAsync();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Second season", title.Text);
        Assert.Equal(3, handler.Requests);
    });

    private sealed class DefinitionsHandler : HttpMessageHandler
    {
        internal string Title = "First season";
        internal int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("/quests/definitions", request.RequestUri!.AbsolutePath);
            Requests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonConvert.SerializeObject(new
                {
                    success = true, version = 1, seasonTitle = Title,
                    seasonKey = DateTime.UtcNow.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                    quests = new { daily = Array.Empty<object>(), weekly = Array.Empty<object>(), seasonal = Array.Empty<object>() },
                })),
            });
        }
    }

    private static void WritePng(string path, int width, int height)
    {
        using var bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96));
        bitmap.Save(path);
    }

    private static void Run(Action<QuestsTabView, QuestService, AppSettings, string, Window> body) =>
        RunAsync((tab, quests, settings, root, host) =>
        {
            body(tab, quests, settings, root, host);
            return Task.CompletedTask;
        }).GetAwaiter().GetResult();

    private static Task RunAsync(Func<QuestsTabView, QuestService, AppSettings, string, Window, Task> body) =>
        AvaloniaTestDispatcher.RunAsync(async () =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var root = Directory.CreateTempSubdirectory("quest-art-").FullName;
            var oldProvider = CoreSettings.ServiceProvider;
            var oldQuests = AvApp.Quests;
            var oldDefinitions = AvApp.QuestDefinitions;
            var oldMod = CoreMods.ActiveModIdProvider;
            AvApp.QuestDefinitions = null;
            var oldArt = CoreModArt.OverridePathProvider;
            var settings = new SettingsService();
            CoreSettings.ServiceProvider = () => settings;
            var quests = new QuestService(null, root);
            quests.Progress.DailyQuests = new List<ActiveQuest> { new("pop_parade_d"), new("flash_rush_d"), new("spiral_sink_d") };
            quests.Progress.WeeklyQuest = new ActiveQuest("flash_monsoon_w");
            AvApp.Quests = quests;
            var tab = new QuestsTabView();
            var host = new Window { Content = tab, Width = 1200, Height = 900 };
            try { await body(tab, quests, settings.Current, root, host); }
            finally
            {
                host.Close();
                quests.Dispose();
                AvApp.Quests = oldQuests;
                AvApp.QuestDefinitions = oldDefinitions;
                CoreMods.ActiveModIdProvider = oldMod;
                CoreModArt.OverridePathProvider = oldArt;
                CoreSettings.ServiceProvider = oldProvider;
                Directory.Delete(root, true);
            }
        });
}
