using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Companion;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class CompanionPickerHostTests
{
    internal static void Setup()
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        else if (!FontManager.Current.TryGetGlyphTypeface(new Typeface(AppFonts.Inter), out _))
        {
            // Other classes can bootstrap the shared app without its bundled font collection.
            var builder = global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp();
            builder.AfterSetupCallback(builder);
        }
    }

    [Fact]
    public void BrowsingAnotherModShowsItsReadOnlyCompanionAndReturningRestoresLive() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        var active = new ModPackage(new ModManifest { Id = "active-picker", Name = "Active" }, null, false);
        var other = new ModPackage(new ModManifest
        {
            Id = "preview-picker", Name = "Preview", Identity = new ModIdentity { CompanionName = "Pip" },
            Personalities = new() { new ModPersonality { Id = "p1", Name = "Pip", SampleLines = new() { "Hello, you." } } },
        }, null, false);
        CoreMods.ActiveModIdProvider = () => active.Id;
        CoreMods.InstalledModsProvider = () => new Dictionary<string, ModPackage> { [active.Id] = active, [other.Id] = other };
        var dialog = new ModManagerDialog();
        try
        {
            var card = dialog.FindControl<CompanionPickerCard>("CompanionCard")!;
            Assert.NotNull(card);
            var list = dialog.FindControl<ListBox>("ModList")!;
            Assert.True(card.FindControl<Control>("LivePanel")!.IsVisible);
            list.SelectedItem = list.Items.OfType<ListBoxItem>().Single(i => (string)i.Tag! == other.Id);
            Assert.True(card.FindControl<Control>("PreviewPanel")!.IsVisible);
            Assert.False(card.FindControl<Control>("LivePanel")!.IsVisible);
            Assert.Equal("Pip", card.FindControl<TextBlock>("TxtPreviewName")!.Text);
            Assert.Equal("\u201CHello, you.\u201D", card.FindControl<TextBlock>("TxtPreviewSample")!.Text);
            Assert.False(card.FindControl<Button>("BtnPerkChange")!.IsVisible);
            Assert.Null(card.MorePersonalityOptions);
            Assert.Equal(active.Id, CoreMods.ActiveModId);
            list.SelectedItem = list.Items.OfType<ListBoxItem>().Single(i => (string)i.Tag! == active.Id);
            Assert.True(card.FindControl<Control>("LivePanel")!.IsVisible);
            Assert.False(card.FindControl<Control>("PreviewPanel")!.IsVisible);
        }
        finally { dialog.Close(); }
    });

    [Theory]
    [InlineData(MotionLevel.Full)]
    [InlineData(MotionLevel.Reduced)]
    [InlineData(MotionLevel.Off)]
    public void PortraitFadesFromPointTwoFor160MillisecondsUnlessMotionIsOff(MotionLevel motion) => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        CoreSettings.Current.MotionLevel = motion;
        var dir = Directory.CreateTempSubdirectory("ccp-picker-portrait-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "resources"));
            using (var bitmap = new WriteableBitmap(new PixelSize(2, 2), new Vector(96, 96)))
                bitmap.Save(Path.Combine(dir, "resources", "avatar_pose1.png"));
            var card = new CompanionPickerCard();
            var image = card.FindControl<Image>("ImgPreview")!;
            var seen = new List<double>();
            image.PropertyChanged += (_, e) => { if (e.Property == Visual.OpacityProperty) seen.Add(image.Opacity); };
            card.ShowPreview(new ModPackage(new ModManifest { Id = "portrait-preview", Name = "Preview" }, dir, false), Colors.HotPink);
            Assert.NotNull(image.Source);
            if (motion == MotionLevel.Off)
            {
                Assert.Null(image.Transitions);
                Assert.DoesNotContain(0.2, seen);
            }
            else
            {
                var fade = Assert.IsType<DoubleTransition>(Assert.Single(image.Transitions!));
                Assert.Equal(Visual.OpacityProperty, fade.Property);
                Assert.Equal(TimeSpan.FromMilliseconds(160), fade.Duration);
                Assert.Contains(0.2, seen);
            }
            Assert.Equal(1.0, image.GetBaseValue(Visual.OpacityProperty).Value); // final fade target
            card.ShowPreview(new ModPackage(new ModManifest { Id = "no-portrait", Name = "No portrait" }, null, false), Colors.HotPink);
            Assert.Null(image.Source);
            Assert.Null(image.Transitions);
            Assert.Equal(1, image.Opacity);
        }
        finally { Directory.Delete(dir, true); }
    });

    // WPF CompanionService.cs:183-209: a successful switch restarts the drain timer.
    [Fact]
    public void CompanionSwitchRestartsDrainButSelectingTheActiveCompanionDoesNot() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        CoreSettings.Current.CompanionPerk = CompanionBonusType.XPDrain;
        DispatcherTimer? Drain() => (DispatcherTimer?)typeof(CompanionHead)
            .GetField("_drainTimer", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null);
        try
        {
            CompanionHead.Start();
            var before = Drain();
            Assert.NotNull(before);
            CoreModsHooks.SwitchCompanion!(CompanionCore.ActiveCompanion);
            Assert.Same(before, Drain());
            var other = Enum.GetValues<CompanionId>().First(id => id != CompanionCore.ActiveCompanion);
            CoreModsHooks.SwitchCompanion!(other);
            Assert.Equal(other, CompanionCore.ActiveCompanion);
            Assert.NotSame(before, Drain());
            Assert.False(before.IsEnabled);
            Assert.True(Drain()!.IsEnabled);
        }
        finally { CompanionHead.Stop(); }
    });

    [Fact]
    public void PerkSelectionRestartsTwoSecondDrainAndLeavingDrainStopsTheOldTimer() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        var s = CoreSettings.Current;
        s.PlayerLevel = 200;
        s.CompanionPerk = CompanionBonusType.XPDrain;
        CompanionHead.Stop();
        DispatcherTimer? Drain() => (DispatcherTimer?)typeof(CompanionHead)
            .GetField("_drainTimer", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null);
        void Pick(CompanionBonusType perk)
        {
            var picker = PerkPicker.Build();
            var radio = picker.GetLogicalDescendants().OfType<RadioButton>().Single(r => (CompanionBonusType)r.Tag! == perk);
            radio.IsChecked = false;
            radio.IsChecked = true;
        }
        try
        {
            CompanionHead.Start();
            var before = Drain();
            Assert.NotNull(before);
            Assert.True(before.IsEnabled);
            CoreModsHooks.SwitchCompanion!(CompanionCore.ActiveCompanion);
            Assert.Same(before, Drain()); // an already-active companion must not postpone the next drain
            Pick(CompanionBonusType.XPDrain);
            var restarted = Drain();
            Assert.NotNull(restarted);
            Assert.NotSame(before, restarted);
            Assert.False(before.IsEnabled);
            Assert.True(restarted.IsEnabled);
            Assert.Equal(TimeSpan.FromSeconds(2), restarted.Interval);
            // A locked choice changes neither the saved perk nor its running cadence.
            s.PlayerLevel = 0;
            Assert.False(CompanionHead.SetPerk(CompanionBonusType.StrictModeBonus));
            Assert.Same(restarted, Drain());
            Assert.Equal(CompanionBonusType.XPDrain, s.CompanionPerk);
            s.PlayerLevel = 200;
            Pick(CompanionBonusType.PinkFilterBonus);
            Assert.Equal(CompanionBonusType.PinkFilterBonus, s.CompanionPerk);
            Assert.False(restarted.IsEnabled);
            Assert.Null(Drain());
            Assert.True(CompanionHead.Started); // active-time timer remains alive
        }
        finally { CompanionHead.Stop(); }
    });

}
