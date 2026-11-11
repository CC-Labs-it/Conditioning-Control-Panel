using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Companion;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF CompanionPickerCard: the personality combo
/// writes through the shell's explicit-content gate, sample lines follow the pick, a non-active mod
/// gets the read-only preview face, and the perk door writes CompanionPerk.</summary>
public sealed class CompanionPickerCardTests
{
    private static async Task WaitFor(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Yield();   // no sleeps in tests (P08); bounded by time (P73)
        }
        Assert.True(condition(), "timed out");
    }

    private static string[] Samples(CompanionPickerCard card) =>
        card.FindControl<StackPanel>("SamplePanel")!.Children.OfType<TextBlock>().Select(t => t.Text ?? "").ToArray();

    [Fact]
    public Task PickingAPersonalityWritesSettingsThroughTheGate() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = CoreSettings.Current;
        s.ActivePersonalityPresetId = PersonalityPresets.NeutralDefaultId;
        s.ActiveCommunityPromptId = null;
        s.CompanionPrompt.UseCustomPrompt = false;
        s.CompanionPrompt.ExplicitContentAcknowledged = false;
        s.SlutModeEnabled = false;
        var oldPerk = s.CompanionPerk;
        var oldLevel = s.PlayerLevel;

        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            // No Avalonia host yet (lane companion-picker-host): a window owned by the shell, as the
            // Customise dialog is in WPF, so the card finds the shell's gate the same way.
            var host = new Window { Width = 900, Height = 500 };
            var card = new CompanionPickerCard();
            host.Content = card;
            host.Show(shell);
            Dispatcher.UIThread.RunJobs();
            var combo = card.FindControl<ComboBox>("CmbPersonality")!;
            Assert.True(card.FindControl<Control>("LivePanel")!.IsVisible);   // active mod: live face
            Assert.Same(shell, card.Shell);
            Assert.False(card.FindControl<Button>("BtnMorePersonality")!.IsVisible);   // no host editor set

            // The combo lists the service's presets with the active one selected.
            var all = PersonalityService.Shared.GetAllPresets();
            Assert.Equal(all.Select(p => p.Id), combo.Items.OfType<ComboBoxItem>().Select(i => (string)i.Tag!));
            Assert.Equal(PersonalityPresets.NeutralDefaultId, (combo.SelectedItem as ComboBoxItem)?.Tag);

            // A plain preset switches, is written to settings, and the sample lines follow it.
            var plain = all.First(p => p.Id != PersonalityPresets.NeutralDefaultId
                                       && !ExplicitContentGate.RequiresAcknowledgement(p, false)
                                       && PersonalitySamples.For(p).Count > 0);
            combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().Single(i => (string)i.Tag! == plain.Id);
            await WaitFor(() => s.ActivePersonalityPresetId == plain.Id);
            await WaitFor(() => Samples(card).Length == PersonalitySamples.For(plain).Count);
            Assert.Equal(PersonalitySamples.For(plain).Select(l => "\u201C" + CoreMods.MakeModAware(l) + "\u201D"), Samples(card));

            // Spice on: an explicit preset asks first; Cancel leaves the old preset and puts the combo back.
            s.SlutModeEnabled = true;
            var spicy = all.First(p => p.Id != plain.Id && ExplicitContentGate.RequiresAcknowledgement(p, true));
            combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().Single(i => (string)i.Tag! == spicy.Id);
            await WaitFor(() => shell.OwnedWindows.OfType<ExplicitContentAcknowledgementDialog>().Any());
            shell.OwnedWindows.OfType<ExplicitContentAcknowledgementDialog>().Single()
                .FindControl<Button>("BtnCancel")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitFor(() => (combo.SelectedItem as ComboBoxItem)?.Tag as string == plain.Id);
            Assert.Equal(plain.Id, s.ActivePersonalityPresetId);
            s.SlutModeEnabled = false;

            // The perk door writes the choice (WPF CompanionService.SetPerk's rule).
            s.PlayerLevel = 100;
            var picker = PerkPicker.Build();
            var target = Enum.GetValues<CompanionBonusType>().First(t => t != CompanionPerks.Active(s));
            picker.GetLogicalDescendants().OfType<RadioButton>().Single(r => (CompanionBonusType)r.Tag! == target).IsChecked = true;
            Assert.Equal(target, s.CompanionPerk);

            // Another mod (WPF ModManagerDialog ShowModDetails' non-active branch): the read-only preview face, its
            // ModPersonality.SampleLines first line in quotes, nothing activated.
            var active = CoreMods.ActiveModId;
            var manifest = new ModManifest
            {
                Id = "picker-preview-mod", Name = "Preview Mod",
                Identity = new ModIdentity { CompanionName = "Pip" },
                Personalities = new() { new ModPersonality { Id = "p1", Name = "Pip", SampleLines = new() { " Hello, you. ", "" } } },
            };
            card.ShowPreview(new ModPackage(manifest, null, false), global::Avalonia.Media.Colors.HotPink);
            Assert.True(card.FindControl<Control>("PreviewPanel")!.IsVisible);
            Assert.False(card.FindControl<Control>("LivePanel")!.IsVisible);
            Assert.Equal("Pip", card.FindControl<TextBlock>("TxtPreviewName")!.Text);
            Assert.Equal("\u201CHello, you.\u201D", card.FindControl<TextBlock>("TxtPreviewSample")!.Text);
            Assert.False(card.FindControl<Button>("BtnPerkChange")!.IsVisible);
            Assert.Equal(active, CoreMods.ActiveModId);
        }
        finally
        {
            s.CompanionPerk = oldPerk;
            s.PlayerLevel = oldLevel;
            s.SlutModeEnabled = false;
            s.ActivePersonalityPresetId = PersonalityPresets.NeutralDefaultId;
            foreach (var w in shell.OwnedWindows.ToList()) w.Close();
            shell.Close();
            Dispatcher.UIThread.RunJobs();
            service.SaveImmediate();
            CoreSettings.ServiceProvider = null;
        }
    });

    [Fact]
    public Task AnimatedModPreviewsUseIdleAndMissingClipFallsBackToPose() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ccp-preview-" + Guid.NewGuid());
        var resources = System.IO.Path.Combine(root, "resources");
        var emotes1 = System.IO.Path.Combine(resources, "emotes", "set1");
        var emotes2 = System.IO.Path.Combine(resources, "emotes", "set2");
        System.IO.Directory.CreateDirectory(emotes1);
        System.IO.Directory.CreateDirectory(emotes2);
        var assets = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Here())!, "..", "..", "Assets"));
        var bundledPath = System.IO.Path.Combine(assets, "avatar0_emotes", "idle.gif");
        var local1Path = System.IO.Path.Combine(emotes1, "idle.gif");
        var local2Path = System.IO.Path.Combine(emotes2, "idle.gif");
        System.IO.File.Copy(System.IO.Path.Combine(assets, "avatar0_emotes", "idle2.gif"), local1Path);
        System.IO.File.Copy(System.IO.Path.Combine(assets, "avatar0_emotes", "idle3.gif"), local2Path);
        System.IO.File.WriteAllText(System.IO.Path.Combine(emotes1, "emotes.json"), "{}");
        System.IO.File.WriteAllText(System.IO.Path.Combine(emotes2, "emotes.json"), "{}");
        var posePath = System.IO.Path.Combine(resources, "avatar_pose1.png");
        System.IO.File.Copy(System.IO.Path.Combine(assets, "avatar_pose1.png"), posePath);
        var mod = new ModPackage(new ModManifest { Id = BuiltInMods.CCPDefaultId, Name = "Preview" }, root, false);
        var old = (CoreMods.ActiveModPackageProvider, CoreMods.ActiveModIdProvider, CoreSettings.Current.SelectedAvatarSet);
        try
        {
            CoreMods.ActiveModPackageProvider = () => mod;
            CoreMods.ActiveModIdProvider = () => mod.Id;
            CoreSettings.Current.SelectedAvatarSet = 1;
            var card = new CompanionPickerCard();
            using var local1 = Decode(local1Path);
            using var local2 = Decode(local2Path);
            using var bundled = Decode(bundledPath);
            using var pose = Decode(posePath);
            Assert.Equal(local1.PixelSize, bundled.PixelSize);
            Assert.False(Pixels(local1).SequenceEqual(Pixels(bundled))); // Same size, different art.
            Assert.False(Pixels(local1).SequenceEqual(Pixels(local2)));
            global::Avalonia.Media.Imaging.Bitmap Picture() => Assert.IsType<global::Avalonia.Media.Imaging.Bitmap>(card.FindControl<Image>("ImgPreview")!.Source);
            void Matches(global::Avalonia.Media.Imaging.Bitmap expected)
            {
                var actual = Picture();
                Assert.Equal(expected.PixelSize, actual.PixelSize);
                Assert.Equal(Pixels(expected), Pixels(actual));
            }
            Matches(local1); // active set 1: local overrides bundled
            card.ShowPreview(mod, global::Avalonia.Media.Colors.HotPink);
            Matches(local1); // inactive set 1: local overrides bundled
            CoreSettings.Current.SelectedAvatarSet = 2;
            card.Refresh();
            Matches(local2); // active selected set 2
            System.IO.File.Delete(local1Path);
            card.ShowPreview(mod, global::Avalonia.Media.Colors.HotPink);
            Matches(bundled); // bundled registry after local clip is removed
            var unregistered = new ModPackage(new ModManifest { Id = "preview-test", Name = "Preview" }, root, false);
            card.ShowPreview(unregistered, global::Avalonia.Media.Colors.HotPink);
            Matches(pose); // no local clip or registry entry
        }
        finally
        {
            (CoreMods.ActiveModPackageProvider, CoreMods.ActiveModIdProvider, CoreSettings.Current.SelectedAvatarSet) = old;
            System.IO.Directory.Delete(root, true);
        }
        return Task.CompletedTask;
    });

    private static global::Avalonia.Media.Imaging.Bitmap Decode(string path)
    {
        using var stream = System.IO.File.OpenRead(path);
        return global::Avalonia.Media.Imaging.Bitmap.DecodeToWidth(stream, 240);
    }

    private static byte[] Pixels(global::Avalonia.Media.Imaging.Bitmap bitmap)
    {
        var size = bitmap.PixelSize;
        var pixels = new byte[size.Width * size.Height * 4];
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(pixels, System.Runtime.InteropServices.GCHandleType.Pinned);
        try { bitmap.CopyPixels(new PixelRect(size), handle.AddrOfPinnedObject(), pixels.Length, size.Width * 4); }
        finally { handle.Free(); }
        return pixels;
    }

    private static string Here([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;

    /// <summary>WPF AvatarSetTitle (AvatarTubeWindow.Avatar.cs:940-941): the Look label is the
    /// companion's name, not the tube's upper-cased caption.</summary>
    [Fact]
    public void LookLabelIsNotUpperCased()
    {
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        try
        {
            var name = CompanionDefinition.GetById(CompanionId.CultBunny).GetDisplayName(CoreSettings.Current.SlutModeEnabled);
            Assert.NotEqual(name, name.ToUpperInvariant());
            Assert.Equal(name, global::ConditioningControlPanel.Avalonia.Views.AvatarTube.AvatarTubeWindow.AvatarSetTitle(4));
        }
        finally { CoreSettings.ServiceProvider = null; }
    }

    /// <summary>The async void link handler must not crash the app when the editor throws: WPF's
    /// sync handler reaches App.xaml.cs:2425 (log + msg_unexpected_error dialog), so the port shows it.</summary>
    [Fact]
    public Task MorePersonalityFailureShowsErrorInsteadOfCrashing() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var oldProvider = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var host = new Window { Width = 900, Height = 500 };
        try
        {
            var card = new CompanionPickerCard { MorePersonalityOptions = _ => throw new InvalidOperationException("boom") };
            host.Content = card;
            host.Show();
            Dispatcher.UIThread.RunJobs();
            card.FindControl<Button>("BtnMorePersonality")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitFor(() => host.OwnedWindows.Any());
            var dialog = Assert.IsType<MessageDialog>(Assert.Single(host.OwnedWindows));
            Assert.Equal(global::ConditioningControlPanel.Localization.Loc.Get("title_unexpected_error"), dialog.Title);
            Assert.Contains("boom", dialog.FindControl<TextBlock>("TxtMessage")!.Text);
        }
        finally
        {
            foreach (var w in host.OwnedWindows.ToList()) w.Close();
            host.Close();
            Dispatcher.UIThread.RunJobs();
            CoreSettings.ServiceProvider = oldProvider;
        }
    });

    /// <summary>The combo's async void handler, same guard: WPF CmbPersonality_SelectionChanged
    /// (CompanionPickerCard.xaml.cs:315) is sync, so a throw from the switch reaches App.xaml.cs:2425.</summary>
    [Fact]
    public Task PersonalitySwitchFailureShowsErrorInsteadOfCrashing() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var oldProvider = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = CoreSettings.Current;
        var oldPreset = s.ActivePersonalityPresetId;
        var oldSlut = s.SlutModeEnabled;
        s.ActivePersonalityPresetId = PersonalityPresets.NeutralDefaultId;
        s.SlutModeEnabled = false;
        EventHandler<PersonalityPreset> boom = (_, _) => throw new InvalidOperationException("boom");
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            var host = new Window { Width = 900, Height = 500 };
            var card = new CompanionPickerCard();
            host.Content = card;
            host.Show(shell);
            Dispatcher.UIThread.RunJobs();
            var plain = PersonalityService.Shared.GetAllPresets().First(p => p.Id != PersonalityPresets.NeutralDefaultId
                                                                            && !ExplicitContentGate.RequiresAcknowledgement(p, false));
            PersonalityService.Shared.PersonalityChanged += boom;
            var combo = card.FindControl<ComboBox>("CmbPersonality")!;
            combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().Single(i => (string)i.Tag! == plain.Id);
            await WaitFor(() => host.OwnedWindows.Any());
            var dialog = Assert.IsType<MessageDialog>(Assert.Single(host.OwnedWindows));
            Assert.Equal(global::ConditioningControlPanel.Localization.Loc.Get("title_unexpected_error"), dialog.Title);
            Assert.Contains("boom", dialog.FindControl<TextBlock>("TxtMessage")!.Text);
        }
        finally
        {
            PersonalityService.Shared.PersonalityChanged -= boom;
            s.ActivePersonalityPresetId = oldPreset;
            s.SlutModeEnabled = oldSlut;
            foreach (var w in shell.OwnedWindows.SelectMany(o => o.OwnedWindows.Prepend(o)).Reverse().ToList()) w.Close();
            shell.Close();
            Dispatcher.UIThread.RunJobs();
            CoreSettings.ServiceProvider = oldProvider;
        }
    });
}
