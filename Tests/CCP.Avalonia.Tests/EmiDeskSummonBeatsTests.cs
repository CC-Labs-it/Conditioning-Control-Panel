using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Services.EmiDesk;
using Newtonsoft.Json;
using Xunit;

namespace CCP.Avalonia.Tests;

[Collection(RunsAloneCollection.Name)]
public class EmiDeskSummonBeatsTests
{
    [Fact]
    public Task EmptyLibraryTestLeavesSharedGreetingsAndStateUntouched() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        using var scope = new EmiTestScope();
        var engine = EmiLineEngine.Instance;
        Assert.True(engine.Ready);
        Assert.False(engine.EverSpent("firstContact"));
        Assert.False(engine.EverSpent("desktopFirstBoot"));
        var state = EmiState.Current;
        EmiState.SaveNow();
        var before = JsonConvert.SerializeObject(state);
        var file = File.ReadAllBytes(EmiState.FilePath);

        EmptyLibraryIsRecheckedAndDismissCancels();

        Assert.Same(state, EmiState.Current);
        Assert.False(engine.EverSpent("firstContact"));
        Assert.False(engine.EverSpent("desktopFirstBoot"));
        Assert.Equal(before, JsonConvert.SerializeObject(state));
        await Task.Delay(750); // The state save is debounced by 500 ms.
        Assert.Equal(file, File.ReadAllBytes(EmiState.FilePath));
    });

    [Fact]
    public void EmptyLibraryTestRestoresSpeechProbes() => AvaloniaTestDispatcher.Run(() =>
    {
        _ = EmiDeskService.Instance; // Its initialization installs the production probes.
        var oldAsk = EmiLineEngine.AskSituationProbe;
        var oldEffect = EmiLineEngine.EffectFeasibleProbe;
        Func<bool> ask = () => true;
        Func<string?, bool> effect = _ => true;
        try
        {
            EmiLineEngine.AskSituationProbe = ask;
            EmiLineEngine.EffectFeasibleProbe = effect;
            EmptyLibraryIsRecheckedAndDismissCancels();
            Assert.Multiple(
                () => Assert.Same(ask, EmiLineEngine.AskSituationProbe),
                () => Assert.Same(effect, EmiLineEngine.EffectFeasibleProbe));
        }
        finally
        {
            EmiLineEngine.AskSituationProbe = oldAsk;
            EmiLineEngine.EffectFeasibleProbe = oldEffect;
        }
    });

    [Fact]
    public void EmptyLibraryIsRecheckedAndDismissCancels() => AvaloniaTestDispatcher.Run(() =>
    {
        var root = Directory.CreateTempSubdirectory("ccp-emi-empty-").FullName;
        var previous = CorePaths.EffectiveAssetsProvider;
        var source = CoreSettings.Current.MediaSource;
        _ = EmiDeskService.Instance;
        var oldAsk = EmiLineEngine.AskSituationProbe;
        var oldEffect = EmiLineEngine.EffectFeasibleProbe;
        EmiDeskService? desk = null;
        try
        {
            desk = new EmiDeskService();
            var engine = EmiLineEngine.FromJson("""
                {"version":1,"moments":{
                  "firstContact":{"priority":3,"limit":{"per":"ever","max":1},"pools":["g"]},
                  "desktopFirstBoot":{"priority":3,"limit":{"per":"ever","max":1},"pools":["g"]}
                },"pools":{"g":[{"id":"g","t":"Hello","spice":0}]}}
                """);
            desk.LineEngine = engine;
            var seen = new List<string>();
            desk.EmptyLibraryMoment = seen.Add;
            typeof(EmiDeskService).GetProperty(nameof(EmiDeskService.IsOut))!.SetValue(desk, true);
            CorePaths.EffectiveAssetsProvider = () => root;
            CoreSettings.Current.MediaSource = "local";
            desk.GreetNow();
            Assert.True(engine.EverSpent("firstContact"));
            var timer = (DispatcherTimer)typeof(EmiDeskService).GetField("_emptyLibraryTimer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(desk)!;
            Assert.Equal(TimeSpan.FromSeconds(7), timer.Interval);
            desk.EmptyLibraryDue(); desk.EmptyLibraryDue();
            Assert.Equal(new[] { "noMediaYet" }, seen);
            seen.Clear();
            desk.GreetNow();
            Assert.True(engine.EverSpent("desktopFirstBoot"));
            Directory.CreateDirectory(Path.Combine(root, "images"));
            File.WriteAllText(Path.Combine(root, "images", "new.png"), "x");
            desk.EmptyLibraryDue();
            Assert.Empty(seen);
            File.Delete(Path.Combine(root, "images", "new.png"));
            CoreSettings.Current.MediaSource = "online";
            desk.GreetNow(); desk.EmptyLibraryDue();
            Assert.Empty(seen);
            CoreSettings.Current.MediaSource = "local";
            desk.GreetNow(); desk.Dismiss(); desk.EmptyLibraryDue();
            Assert.Empty(seen);
            Directory.CreateDirectory(Path.Combine(root, "videos"));
            File.WriteAllText(Path.Combine(root, "videos", "new.mp4"), "x");
            Assert.False(EmiOffers.LibraryIsEmpty());
        }
        finally
        {
            desk?.Dismiss();
            EmiLineEngine.AskSituationProbe = oldAsk;
            EmiLineEngine.EffectFeasibleProbe = oldEffect;
            CorePaths.EffectiveAssetsProvider = previous;
            CoreSettings.Current.MediaSource = source;
            Directory.Delete(root, true);
        }
    });
}
