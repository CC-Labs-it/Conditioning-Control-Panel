using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ChaosHubCatalogueTests
{
    // WPF ChaosHubWindow.xaml.cs:1258-1349. Real catalogue, persisted choice, no curse/hidden picks.
    [Fact]
    public Task Mantras_use_discovery_and_save_toggle_in_the_active_slot() => InHub(state =>
    {
        foreach (var b in ChaosBoonPool.All.Where(b => b.IsCurse || b.Id == "golden_touch"))
            state.DiscoveredCodexIds.Add("boon:" + b.Id);
    }, hub =>
    {
        var host = hub.FindControl<StackPanel>("MantrasHost")!;
        Assert.Equal(ChaosBoonPool.All.Count, host.Children.Count);
        var boon = ChaosBoonPool.All.Single(b => b.Id == "golden_touch");
        Border Row(string id) => (Border)host.Children[ChaosBoonPool.All.FindIndex(b => b.Id == id)];
        Assert.Contains(boon.Desc, Texts(Row(boon.Id)));
        var hidden = ChaosBoonPool.All.First(b => !ChaosMeta.IsDiscovered("boon:" + b.Id));
        Assert.Contains("???", Texts(Row(hidden.Id)));
        Assert.DoesNotContain(hidden.Name, Texts(Row(hidden.Id)));
        Press(hub, Row(hidden.Id));
        Assert.Null(ChaosMeta.State.EquippedStartBoon);
        Press(hub, Row(ChaosBoonPool.All.First(b => b.IsCurse).Id));
        Assert.Null(ChaosMeta.State.EquippedStartBoon);
        Press(hub, Row(boon.Id), false);
        Assert.Null(ChaosMeta.State.EquippedStartBoon);
        Press(hub, Row(boon.Id));
        Assert.Equal(boon.Id, ChaosMetaStore.Load(1).EquippedStartBoon);
        Assert.Equal(3, Row(boon.Id).BorderThickness.Left);
        Press(hub, Row(boon.Id));
        Assert.Null(ChaosMetaStore.Load(1).EquippedStartBoon);
        Assert.Equal(2, Row(boon.Id).BorderThickness.Left);
    });

    // WPF ChaosHubWindow.xaml.cs:1374-1395 and :1492: every pool/special entry gates by discovery.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Diary_lists_the_real_pool_and_specials_without_leaking_hidden_names(bool seen) => InHub(state =>
    {
        if (seen) foreach (var id in BubbleIds()) state.DiscoveredCodexIds.Add("bubble:" + id);
    }, hub =>
    {
        var host = hub.FindControl<StackPanel>("DiaryHost")!;
        var rows = host.Children.OfType<Border>().TakeLast(BubbleIds().Count()).ToArray();
        Assert.Equal(BubbleIds().Count(), rows.Length);
        foreach (var pair in BubbleIds().Zip(rows))
        {
            var texts = Texts(pair.Second);
            Assert.Contains(seen ? ChaosBubbleVariants.DescriptionFor(pair.First) : "hazy. go back down and look closer.", texts);
            if (!seen) Assert.Contains("???", texts);
        }
        if (seen)
        {
            foreach (var v in ChaosBubbleVariants.All) Assert.Contains(v.Name, Texts(host));
            Assert.Contains("White Rabbit", Texts(host));
            Assert.Contains("The Brittle", Texts(host));
        }
    });

    // WPF ChaosHubWindow.Bench.cs:208-220: short buys sound denied, gated rows stay silent.
    [Theory]
    [InlineData("short", "ui_denied", 0.45f)]
    [InlineData("owned", null, 0f)]
    [InlineData("rank", null, 0f)]
    [InlineData("buy", "ui_unlock", 0.55f)]
    [InlineData("pocket", "pocket_sewn", 0.7f)]
    public Task Bench_plays_only_the_wpf_purchase_cue(string scenario, string? cue, float scale) => InHub(state =>
    {
        state.Gold = scenario == "short" ? 0 : 10000;
        if (scenario == "owned") state.BenchPurchases.Add(BenchIds.Diary);
    }, hub =>
    {
        var oldAudio = CoreAudio.PlayOneShotProvider;
        var oldVolume = CoreSettings.Current.MasterVolume;
        var created = new List<string>();
        var heard = new List<(string, float)>();
        try
        {
            foreach (var key in new[] { "ui_denied", "ui_unlock", "pocket_sewn" })
            {
                var path = Path.Combine(ContentLocator.InstallRoot, "Resources", "sounds", "chaos", key + ".mp3");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                if (!File.Exists(path)) { File.WriteAllBytes(path, new byte[] { 0 }); created.Add(path); }
            }
            CoreSettings.Current.MasterVolume = 100;
            CoreAudio.PlayOneShotProvider = (path, volume, tag, started, ended) => heard.Add((Path.GetFileNameWithoutExtension(path), volume));
            var id = scenario == "rank" ? BenchIds.ToyPocket2 : scenario == "pocket" ? BenchIds.ToyPocket1 : BenchIds.Diary;
            // Gated rows have no buy button. Invoke the same handler to prove their silent guard.
            if (scenario is "owned" or "rank")
                typeof(ChaosHubWindow).GetMethod("BenchBuy", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(hub, new object[] { id });
            else
                hub.GetLogicalDescendants().OfType<Button>().First(b => b.Tag as string == id && b.Content is StackPanel p
                        && p.Children.OfType<TextBlock>().Any(t => t.Text == "buy"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (cue == null) Assert.Empty(heard);
            else Assert.Equal((cue, scale), Assert.Single(heard));
            if (scenario is "buy" or "pocket") Assert.Contains(id, ChaosMetaStore.Load(1).BenchPurchases);
            if (scenario == "pocket") Assert.NotNull(hub.UnlockCards.Current);
        }
        finally
        {
            CoreAudio.PlayOneShotProvider = oldAudio;
            CoreSettings.Current.MasterVolume = oldVolume;
            foreach (var path in created) File.Delete(path);
        }
    });

    private static IEnumerable<string> BubbleIds() => ChaosBubbleVariants.All.Select(v => v.Id)
        .Concat(new[] { "darter", "golden", "echo", "chaperone", "tease", "bound", "brittle" });
    private static string?[] Texts(Control c) => c.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text).ToArray();
    private static void Press(Window hub, Control target, bool left = true)
    {
        var pointer = new global::Avalonia.Input.Pointer(global::Avalonia.Input.Pointer.GetNextFreeId(), PointerType.Mouse, true);
        target.RaiseEvent(new PointerPressedEventArgs(target, pointer, hub, default, 0,
            new PointerPointProperties(left ? RawInputModifiers.LeftMouseButton : RawInputModifiers.RightMouseButton,
                left ? PointerUpdateKind.LeftButtonPressed : PointerUpdateKind.RightButtonPressed), KeyModifiers.None, 1));
    }
    private static Task InHub(Action<ChaosMetaState> arrange, Action<ChaosHubWindow> check) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var oldSlot = CoreSettings.Current.ChaosActiveSlot;
        var oldState = ChaosMetaStore.Load(1);
        var state = new ChaosMetaState { RunsCompleted = 25, Gold = 10000 };
        arrange(state);
        ChaosMetaStore.Save(state, 1);
        ChaosMetaStore.SetActiveSlot(1);
        var hub = new ChaosHubWindow();
        try { check(hub); }
        finally { hub.Close(); ChaosMetaStore.Save(oldState, 1); ChaosMetaStore.SetActiveSlot(oldSlot); ChaosMeta.Init(); }
        return Task.CompletedTask;
    });
}
