using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.LogicalTree;
using System.Linq;
using System.Collections.Generic;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Descent;
using Newtonsoft.Json.Linq;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

[Collection(RunsAloneCollection.Name)]
public sealed class ProfileSpiralParityTests
{
    private static Task Run(Func<SettingsService, DescentService, MainShellWindow, Task> body) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current == null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var oldSettings = CoreSettings.ServiceProvider;
        var oldDescent = AvApp.Descent;
        var oldFuse = AvApp.DescentCountdown;
        var oldRetrieve = CoreSecrets.RetrieveProvider;
        var oldStore = CoreSecrets.StoreProvider;
        var oldPost = CoreDispatch.PostProvider;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        CoreSecrets.RetrieveProvider = _ => "test-token";
        CoreSecrets.StoreProvider = (_, _) => { };
        CoreDispatch.PostProvider = a => Dispatcher.UIThread.Post(a);
        settings.Current.UnifiedId = "spiral-test";
        settings.Current.OfflineMode = false;
        settings.Current.MotionLevel = MotionLevel.Off;
        settings.Current.AvatarEnabled = false;
        settings.Current.DescentMigrationOffered = false;
        settings.Current.DescentMigrationCompleted = false;
        settings.Current.PendingDescentMigrationChoice = null;
        settings.Current.SeenFeatureIntros.AddRange(FeatureIntros.All.Keys);
        AvApp.DescentCountdown = null;
        int reads = 0;
        var descent = new DescentService(_ =>
        {
            var node = JObject.Parse("{\"descent\":{\"devotion_days\":12,\"stage\":{\"n\":3},\"vat\":{\"cap\":4000}}}");
            node["descent"]!["devotion_days"] = 12 + reads++;
            return Task.FromResult<JObject?>(node);
        });
        AvApp.Descent = descent;
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            await body(settings, descent, shell);
        }
        finally
        {
            shell.Close();
            CoreSettings.ServiceProvider = oldSettings;
            AvApp.Descent = oldDescent;
            AvApp.DescentCountdown = oldFuse;
            CoreSecrets.RetrieveProvider = oldRetrieve;
            CoreSecrets.StoreProvider = oldStore;
            CoreDispatch.PostProvider = oldPost;
        }
    });

    private static bool Menu(MainShellWindow shell) => shell.Named<Button>("ProfileMenuSpiralRow")!.IsVisible;
    private static bool Plate(MainShellWindow shell) => shell.ProfilePage!.FindControl<Border>("ProfileSpiralPlate")!.IsVisible;
    private static void Drain() => Dispatcher.UIThread.RunJobs();

    [Fact] // WPF ProfileSpiral.cs:130-162, :182-185; ProfileCard.cs:98.
    public Task ServerArrivalWithdrawalSelfGateAndLocalizedSummary() => Run(async (settings, descent, shell) =>
    {
        Assert.False(Menu(shell));
        Assert.False(Plate(shell));
        // This rail-only flag must not suppress either native profile door.
        settings.Current.DescentSpiralRailEnabled = false;
        Assert.True(await descent.RefreshAsync("test", force: true));
        Drain();
        Assert.True(Menu(shell));
        Assert.True(Plate(shell));
        Assert.Contains(shell.ProfilePage!.FindControl<SpiralGlyph>("ProfileSpiralGlyph")!.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == "III");
        Assert.Contains(shell.Named<SpiralGlyph>("ProfileMenuSpiralGlyph")!.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == "III");
        Assert.Equal(string.Format(Loc.Get("programs_card_day"), 12) + " · " + DescentStageCopy.Name(3),
            shell.Named<TextBlock>("ProfileMenuSpiralSummary")!.Text);
        shell.SetProfileViewingSelf(false);
        Assert.False(Plate(shell));
        Assert.True(Menu(shell));
        shell.SetProfileViewingSelf(true);
        Assert.True(Plate(shell));
        descent.Reset();
        Drain();
        Assert.False(Menu(shell));
        Assert.False(Plate(shell));
    });

    [Fact] // WPF ProfileSpiral.cs:60, :131, :156; UiUpdates.cs motion refresh.
    public Task PersistedMigrationAndSettingsReplacementRepaintBothDoors() => Run(async (settings, descent, shell) =>
    {
        await descent.RefreshAsync("test", force: true);
        Drain();
        settings.Current.DescentMigrationOffered = true;
        Drain();
        Assert.False(Menu(shell));
        Assert.False(Plate(shell));
        settings.Current.PendingDescentMigrationChoice = "invalid";
        Drain();
        Assert.False(Menu(shell));
        settings.Current.PendingDescentMigrationChoice = DescentMigrationChoices.Restore;
        Drain();
        Assert.True(Menu(shell));
        Assert.True(Plate(shell));
        var old = settings.Current;
        settings.RestoreFrom(new AppSettings { DescentMigrationOffered = true, MotionLevel = MotionLevel.Off });
        Drain();
        Assert.False(Menu(shell));
        settings.Current.DescentMigrationCompleted = true;
        Drain();
        Assert.True(Menu(shell));
        old.PendingDescentMigrationChoice = null;
        Drain();
        Assert.True(Menu(shell));
    });

    [Fact] // WPF ProfileSpiral.cs:203-213; DiscordTabView.xaml.cs:139-141; SpiralTabView.xaml.cs:414-420.
    public Task BothDoorsReachRoomAndStaleClickRechecksGates() => Run(async (settings, descent, shell) =>
    {
        await descent.RefreshAsync("test", force: true);
        Drain();
        var popup = shell.FindControl<Popup>("ProfileBubblePopup")!;
        popup.IsOpen = true;
        Assert.True(popup.IsOpen);
        shell.Named<Button>("ProfileMenuSpiralRow")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Drain();
        Assert.Equal(SpiralRoom.TabKey, shell.CurrentTab);
        Assert.False(popup.IsOpen);
        var room = shell.Named<SpiralTabView>("SpiralTab")!;
        Assert.True(room.IsShowingSpiral);
        Assert.False(room.FindControl<Grid>("FogHost")!.IsVisible);
        Assert.True(room.FindControl<Button>("BtnSpiralHelp")!.IsVisible); // Spiral, even if native embed unavailable
        descent.Reset();
        Drain();
        Assert.False(room.IsShowingSpiral);
        Assert.True(room.FindControl<Border>("WaitingPanel")!.IsVisible);
        await descent.RefreshAsync("return", force: true);
        Drain();
        Assert.True(room.IsShowingSpiral);
        shell.ShowTab("discord");
        shell.ProfilePage!.FindControl<Border>("ProfileSpiralPlate")!.RaiseEvent(
            new PointerReleasedEventArgs(shell.ProfilePage, new Pointer(2, PointerType.Mouse, true), shell.ProfilePage, new Point(), 0, new PointerPointProperties(), KeyModifiers.None, MouseButton.Right));
        Assert.Equal("discord", shell.CurrentTab);
        settings.Current.DescentMigrationOffered = true;
        Drain();
        shell.ProfilePage!.FindControl<Border>("ProfileSpiralPlate")!.RaiseEvent(
            new PointerReleasedEventArgs(shell.ProfilePage, new Pointer(1, PointerType.Mouse, true), shell.ProfilePage, new Point(), 0, new PointerPointProperties(), KeyModifiers.None, MouseButton.Left));
        Drain();
        Assert.Equal(SpiralRoom.TabKey, shell.CurrentTab);
        Assert.True(room.FindControl<Grid>("FogHost")!.IsVisible);
        descent.Reset();
        settings.Current.DescentMigrationOffered = false;
        Drain();
        Assert.True(room.FindControl<Border>("WaitingPanel")!.IsVisible);
    });

    [Fact] // WPF SpiralTabView.xaml.cs:249, :307, :892: initial and live server state.
    public Task RoomSeedsTheEmbedAndForwardsLiveBlocks() => Run(async (settings, descent, shell) =>
    {
        var oldStart = SpiralTabView.StartEmbed;
        SpiralEmbedView? embed = null;
        var sent = new List<string>();
        SpiralTabView.StartEmbed = view =>
        {
            embed = view;
            view.SendJson = json => { sent.Add(json); return Task.CompletedTask; };
            view.Receive("{\"type\":\"spiral:ready\"}");
        };
        try
        {
            await descent.RefreshAsync("initial", force: true);
            Drain();
            shell.ShowTab("spiral");
            Drain();
            Assert.NotNull(embed);
            Assert.Equal(12, (int)JObject.Parse(Assert.Single(sent))["devotion_days"]!);
            await descent.RefreshAsync("update", force: true);
            Drain();
            Assert.Equal(13, (int)JObject.Parse(sent.Last())["devotion_days"]!);
            shell.ShowTab("settings");
            var count = sent.Count;
            await descent.RefreshAsync("hidden", force: true);
            Drain();
            Assert.Equal(count, sent.Count);
        }
        finally { SpiralTabView.StartEmbed = oldStart; }
    });

    [Fact] // WPF ProfileSpiral.cs:105-111; MainWindow.UiUpdates.cs:2401.
    public Task LiveMotionChangeRefreshesAnAlreadyVisibleGlyph() => Run(async (settings, descent, shell) =>
    {
        await descent.RefreshAsync("test", force: true);
        shell.ShowTab("discord");
        Drain();
        var glyph = shell.ProfilePage!.FindControl<SpiralGlyph>("ProfileSpiralGlyph")!;
        var oldProbe = global::ConditioningControlPanel.Avalonia.Platform.UiMotionPreference.OsProbe;
        try
        {
            global::ConditioningControlPanel.Avalonia.Platform.UiMotionPreference.OsProbe = () => true;
            global::ConditioningControlPanel.Avalonia.Platform.UiMotionPreference.CacheStamp = long.MinValue;
            settings.Current.GentleVisuals = false;
            settings.Current.PerformanceMode = false;
            settings.Current.MotionLevel = MotionLevel.Full;
            Drain();
            Assert.True(glyph.IsBreathing);
            settings.Current.MotionLevel = MotionLevel.Off;
            Drain();
            Assert.False(glyph.IsBreathing);
        }
        finally
        {
            global::ConditioningControlPanel.Avalonia.Platform.UiMotionPreference.OsProbe = oldProbe;
            global::ConditioningControlPanel.Avalonia.Platform.UiMotionPreference.CacheStamp = long.MinValue;
        }
    });

    [Fact] // WPF ProfileSpiral.cs:83-91: service must not retain a closed shell.
    public Task CloseDetachesBlockAndQueuedSettingsCallbacks() => Run(async (settings, descent, shell) =>
    {
        settings.Current.DescentMigrationOffered = true; // callback queued before close
        shell.Close();
        await descent.RefreshAsync("after close", force: true);
        Drain();
        Assert.False(Menu(shell));
        var field = typeof(DescentService).GetField("BlockChanged", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var callbacks = (MulticastDelegate?)field.GetValue(descent);
        Assert.DoesNotContain(callbacks?.GetInvocationList() ?? Array.Empty<Delegate>(), d => ReferenceEquals(d.Target, shell));
    });
}
