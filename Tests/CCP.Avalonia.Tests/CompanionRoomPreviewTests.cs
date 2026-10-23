using System;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Companion.Brain;
using ConditioningControlPanel.Views.Controls.Companion;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class CompanionRoomPreviewTests
{
    private static CompanionRoomPreviewWindow Create()
    {
        if (Application.Current is null)
            AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        return new CompanionRoomPreviewWindow();
    }

    [Fact]
    public Task StateButtonsSwapChatAndMemoryInsteadOfOnlyTheLabel() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var window = Create();
        try
        {
            var chat = window.RoomView.GetLogicalDescendants().OfType<ChatThresholdView>().Single();
            var diary = window.RoomView.GetLogicalDescendants().OfType<MemoryDiaryView>().Single();
            Assert.Equal(CompanionZoneState.Live, chat.ViewModel!.State);
            Assert.NotEmpty(chat.ViewModel.Turns);
            Assert.False(diary.ViewModel!.IsEmpty);
            var button = window.GetLogicalDescendants().OfType<Button>().Single(b => Equals(b.Tag, "disabled"));
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("disabled", window.CurrentVariantKey);
            Assert.Equal(CompanionZoneState.Disabled, chat.ViewModel!.State);
            Assert.False(chat.ViewModel.CanSend);
            Assert.Empty(chat.ViewModel.Turns);
            Assert.True(window.ShowVariant("freeTier"));
            Assert.Equal(CompanionZoneState.Locked, chat.ViewModel.State);
            Assert.NotEmpty(chat.ViewModel.TeaserTurns);
            Assert.False(diary.ViewModel!.IsEmpty); // The diary is never paywalled.
            Assert.True(window.ShowVariant("empty"));
            Assert.Equal(CompanionZoneState.Live, chat.ViewModel.State);
            Assert.Empty(chat.ViewModel.Turns);
            Assert.True(diary.ViewModel!.IsEmpty);
            Assert.Equal(new[] { "Level 1", "Streak 1 day", "1 session" }, diary.ViewModel.ProfileStats);
            Assert.True(window.ShowVariant("dormant"));
            Assert.Equal(CompanionZoneState.Dormant, chat.ViewModel.State);
            Assert.False(Assert.Single(chat.ViewModel.Turns).IsAiGenerated);
            Assert.Equal(2, diary.ViewModel!.Facts.Count);
            Assert.True(diary.ViewModel.Facts.Any(f => f.IsDormant));
            Assert.True(window.ShowVariant("default"));
            Assert.NotEmpty(chat.ViewModel.Turns);
            Assert.False(diary.ViewModel!.IsEmpty);
        }
        finally { window.Close(); }
        return Task.CompletedTask;
    });

    // WPF MockChatThresholdVm / MockAttentionGaugeVm copy per strip key, and the per-switch wiring.
    [Fact]
    public Task EachVariantSeatsWpfCopyAttentionAndWiring() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var window = Create();
        try
        {
            var chat = window.RoomView.GetLogicalDescendants().OfType<ChatThresholdView>().Single();
            var diary = window.RoomView.GetLogicalDescendants().OfType<MemoryDiaryView>().Single();
            var gauge = window.RoomView.GetLogicalDescendants().OfType<AttentionGaugeView>().Single();
            string lastHeard = Loc.GetF("companion_chat_last_heard_fmt", "2h ago");
            var expected = new (string Key, string State, string Footer, string LastHeard, double Fraction)[]
            {
                ("default", "", Loc.Get("companion_chat_footer_remembers"), lastHeard, 0.72),
                ("freeTier", "", "", "", 0.30),
                ("dormant", Loc.Get("companion_chat_dormant_copy"), "", "", 0.72),
                ("empty", "", Loc.Get("companion_chat_footer_first"), "", 0.72),
                ("drained", "", Loc.Get("companion_chat_footer_remembers"), lastHeard, 0.0),
                ("disabled", Loc.Get("companion_chat_disabled_copy"), "", "", 0.72),
            };
            foreach (var (key, state, footer, heard, fraction) in expected)
            {
                diary.ForgetConfirm.ArmCommand.Execute(null);
                Assert.True(diary.ForgetConfirm.IsArmed, key);
                Assert.True(window.ShowVariant(key));
                var vm = chat.ViewModel!;
                Assert.Equal(state, vm.StateCopy);
                Assert.Equal(footer, vm.FooterCopy);
                Assert.Equal(heard, vm.LastHeardCopy);
                Assert.NotNull(vm.OpenEngineRoom); // Loaded never repeats on a strip click
                Assert.True(vm.OpenEngineRoomCommand.CanExecute(null));
                Assert.False(diary.ForgetConfirm.IsArmed, key);
                window.RoomView.SyncBrain(); // a preview gauge never re-reads the live budget
                var a = gauge.ViewModel!;
                Assert.Equal(fraction, a.Fraction, 3);
                Assert.Equal(Loc.Get(AttentionCopy.CopyKeyFor(fraction)), a.StateCopy);
                Assert.Equal(AttentionCopy.ShowUpsell(fraction), a.ShowUpsell);
            }
            Assert.False(gauge.ViewModel!.IsSpent);
            Assert.True(window.ShowVariant("drained"));
            Assert.True(gauge.ViewModel!.IsSpent);
            Assert.Equal(Loc.Get("companion_attention_detail_line_spent"), gauge.ViewModel.DetailLine);
            Assert.True(window.ShowVariant("freeTier"));
            Assert.True(gauge.ViewModel!.ShowUpsell);
            Assert.Equal(Loc.Get("companion_attention_detail_line"), gauge.ViewModel.DetailLine);
        }
        finally { window.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task InvalidKeyPreservesThePageAndValidKeysCreateFreshMocks() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var window = Create();
        try
        {
            var chat = window.RoomView.GetLogicalDescendants().OfType<ChatThresholdView>().Single();
            Assert.True(window.ShowVariant("EMPTY"));
            var previous = chat.ViewModel!;
            Assert.False(window.ShowVariant(null));
            Assert.False(window.ShowVariant("typo"));
            Assert.Same(previous, chat.ViewModel);
            Assert.Equal("EMPTY", window.CurrentVariantKey);
            previous.Draft = "preview only";
            previous.SendCommand.Execute(null);
            Assert.Single(previous.Turns);
            Assert.True(window.ShowVariant("empty"));
            Assert.NotSame(previous, chat.ViewModel);
            Assert.Empty(chat.ViewModel!.Turns);
            Assert.False(chat.ViewModel.IsAttached);
            var widthToggle = window.FindControl<Button>("WidthToggle")!;
            widthToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1000, window.Width);
            widthToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1240, window.Width);
        }
        finally { window.Close(); }
        return Task.CompletedTask;
    });

    // WPF MockCompanionRoomVm.cs:94-161: hero, personality, awareness and engine exhibits per strip
    // key, the navigator fan-out, and no exhibit control that writes real settings.
    [Fact]
    public Task EachVariantSeatsWpfHeroPersonalityAwarenessAndEngineMocks() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var window = Create();
        var s = ConditioningControlPanel.CoreSettings.Current;
        var before = (s.AvatarMuted, s.AvatarEnabled, s.AiChatEnabled, s.AwarenessRetentionDays, s.SlutModeEnabled);
        try
        {
            window.Show();   // the engine drawer's Expander binding carries the navigator check below
            var room = window.RoomView.GetLogicalDescendants().ToList();
            var heroView = room.OfType<CompanionHeroCard>().Single();
            var personality = room.OfType<MakeHerYoursView>().Single();
            var awareness = room.OfType<AwarenessPrivacyView>().Single();
            var engineView = room.OfType<EngineRoomDrawer>().Single();
            Assert.True(heroView.IsEnabled && personality.IsEnabled && awareness.IsEnabled && engineView.IsEnabled);
            Assert.True(room.OfType<AiPermissionsGrid>().Single().IsEnabled);
            Assert.True(room.OfType<WorkshopAccordion>().Single().IsEnabled);

            var expected = new (string Key, int Level, bool Enabled, bool AiLocked, bool Plate, bool Traits,
                bool Dormant, bool Wire, AwarenessIntensity Dial, CompanionProviderMode Provider, bool LoggedIn)[]
            {
                ("default", 41, true, false, true, true, false, true, AwarenessIntensity.BroadStrokes, CompanionProviderMode.Cloud, true),
                ("freeTier", 41, true, true, false, true, false, true, AwarenessIntensity.BroadStrokes, CompanionProviderMode.Cloud, false),
                ("dormant", 41, true, false, true, false, true, false, AwarenessIntensity.BroadStrokes, CompanionProviderMode.Cloud, true),
                ("empty", 1, true, false, true, false, false, true, AwarenessIntensity.BroadStrokes, CompanionProviderMode.Cloud, true),
                ("drained", 41, true, false, true, true, false, true, AwarenessIntensity.BroadStrokes, CompanionProviderMode.Cloud, true),
                ("disabled", 41, false, false, true, true, false, false, AwarenessIntensity.Off, CompanionProviderMode.Off, true),
            };
            foreach (var e in expected)
            {
                Assert.True(window.ShowVariant(e.Key));
                window.RoomView.SyncBrain();   // a preview exhibit never re-reads the live settings
                var hero = heroView.ViewModel!;
                Assert.Equal(e.Level, hero.Level);
                Assert.Equal(e.Enabled, hero.IsCompanionEnabled);
                Assert.Equal(e.AiLocked, hero.IsAiLocked);
                Assert.Equal(e.Plate, hero.Header!.HasAiAccess);
                Assert.Equal(e.Traits, personality.ViewModel.AreTraitsAvailable);
                Assert.Equal(7, personality.ViewModel.Presets.Count);
                Assert.Equal(e.Dormant, awareness.ViewModel!.IsDormant);
                Assert.Equal(e.Wire, awareness.ViewModel.IsWireLive);
                Assert.Equal(e.Dial, awareness.ViewModel.Intensity);
                var engine = Assert.IsType<EngineRoomVm>(engineView.DataContext);
                Assert.Equal(e.Provider, engine.Provider);
                Assert.Equal(e.LoggedIn, engine.IsLoggedIn);
                Assert.False(engine.IsExpanded);   // WPF MockCompanionRoomVm.Closed: the drawer rests shut
            }
            Assert.Equal("8 / 200 XP", window.ShowVariant("empty") ? heroView.ViewModel!.XpLabel : "");
            Assert.True(window.ShowVariant("default"));
            Assert.Equal(2, awareness.ViewModel!.KnownApps.Count);
            Assert.True(awareness.ViewModel.HasWireJson);

            // Exhibit controls change the exhibit only.
            var heroVm = heroView.ViewModel!;
            heroVm.ToggleMuteCommand.Execute(null);
            heroVm.ToggleShownCommand.Execute(null);
            Assert.True(heroVm.IsMuted);
            Assert.False(heroVm.IsCompanionShown);
            heroVm.WakeCommand.Execute(null);
            awareness.ViewModel.Intensity = AwarenessIntensity.Everything;
            awareness.ViewModel.RetentionDays = 7;
            awareness.ViewModel.PauseCommand.Execute(null);
            Assert.Equal(AwarenessIntensity.Everything, awareness.ViewModel.Intensity);
            Assert.False(awareness.ViewModel.IsPaused);
            var presets = personality.ViewModel.Presets;
            presets[2].IsSelected = true;
            Assert.Equal(new[] { 2 }, presets.Select((p, i) => (p, i)).Where(x => x.p.IsSelected).Select(x => x.i));
            presets[2].IsSelected = false;   // a second click keeps the radio group's one selection
            Assert.True(presets[2].IsSelected);
            personality.ViewModel.IsSpiceOn = true;
            personality.ViewModel.ResetPersonalityCommand.Execute(null);
            var eng = (EngineRoomVm)engineView.DataContext!;
            eng.Provider = CompanionProviderMode.Off;
            eng.OllamaModel = "changed";
            Assert.Equal(before, (s.AvatarMuted, s.AvatarEnabled, s.AiChatEnabled, s.AwarenessRetentionDays, s.SlutModeEnabled));
            Assert.NotEqual("changed", s.CompanionPrompt?.AiModel);

            // Navigator fan-out: the hero's AI pill opens the Engine Room on the page.
            heroVm.OpenEngineRoomCommand.Execute(null);
            Assert.True(eng.IsExpanded);
        }
        finally { window.Close(); }
        return Task.CompletedTask;
    });
    [Fact]
    public void WorkshopExhibitMatchesWpfRowsAndCommandsAreInert()
    {
        var vm = new WorkshopPreviewVm();
        Assert.False(vm.IsExpanded);
        Assert.Equal(Loc.Get("companion_workshop_drawer_note"), vm.DrawerNote);
        Assert.Equal(new[] { "ROSTER", "BEHAVIOR", "TRIGGERS & PHRASES", "HER LIBRARY", "COMMUNITY", "AWARENESS FINE-TUNING" },
            vm.Cells.Select(c => c.Key));
        Assert.All(vm.Cells, c => { Assert.Equal(c.Key, c.Title); Assert.Null(c.Content); });
        Assert.Equal(new[] { 6, 7, 4, 2, 3, 3 }, vm.Cells.Select(c => c.Rows.Count));
        var expected = new (string Label, string? Value)[]
        {
            ("Synthetic Blowdoll", "Lv 12"), ("Perfect Fuckpuppet", "[100]"),
            ("Brainwashed Slavedoll", "[125]"), ("Platinum Puppet", "[150]"), ("Bambi Cow", "[75]"), ("assign personality", null),
            ("Idle interval", "120s"), ("Bubble time", "2s"), ("Chat shortcut", "Ctrl+T"), ("Camera shortcut", "Ctrl+Alt+K"),
            ("Mute whispers", "on"), ("Pause in browser", "off"),
            ("idle interval is set by her Proactivity trait — override here", null),
            ("Trigger mode · interval", "60s"), ("Edit trigger phrases", null), ("Manage phrases", "128 active"),
            ("Phrase presets · save / delete", null), ("Hypnotube link pool", "Bambi Sleep scope"), ("+ add link", "14 links"),
            ("Browse shared companions", null), ("Import / Export", null), ("Refresh installed", "2"),
            ("Cooldown", "90s"), ("Max cooldown", "off"), ("Privacy notice · full text", null)
        };
        var rows = vm.Cells.SelectMany(c => c.Rows).ToArray();
        Assert.Equal(expected, rows.Select(r => (r.Label, r.Value)));
        Assert.Equal(new[] { 0.38, 0.20, 0.45, 0.0 }, rows.Where(r => r.IsSlider).Select(r => r.SliderFraction));
        Assert.Equal(rows[12], Assert.Single(rows.Where(r => r.IsCaption)));
        var before = Newtonsoft.Json.JsonConvert.SerializeObject(ConditioningControlPanel.CoreSettings.Current);
        foreach (var row in rows)
        {
            if (row.IsSlider || row.IsCaption) Assert.Null(row.ActivateCommand);
            else { Assert.True(row.ActivateCommand!.CanExecute(null)); row.ActivateCommand.Execute(null); }
        }
        vm.FocusCellCommand.Execute(CompanionRoomAnchors.WorkshopRosterCell);
        Assert.False(vm.IsExpanded); // WPF mock headings record a tag, never navigate.
        Assert.Equal(before, Newtonsoft.Json.JsonConvert.SerializeObject(ConditioningControlPanel.CoreSettings.Current));
        vm.IsExpanded = true;
        Assert.False(new WorkshopPreviewVm().IsExpanded);
    }

    [Fact]
    public Task EveryVariantSeatsFreshWorkshopAndDeepLinksOpenIt() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var window = Create();
        try
        {
            window.Show();
            var room = window.RoomView;
            var shop = room.FindControl<WorkshopAccordion>("WorkshopZone")!;
            IWorkshopAccordionVm? previous = null;
            foreach (var key in new[] { "default", "freeTier", "dormant", "empty", "drained", "disabled" })
            {
                Assert.True(window.ShowVariant(key));
                Assert.True(shop.IsEnabled);
                var vm = Assert.IsType<WorkshopPreviewVm>(shop.ViewModel);
                Assert.NotSame(previous, vm);
                Assert.False(vm.IsExpanded);
                Assert.All(vm.Cells, c => Assert.Null(c.Content));
                Assert.Equal(6, vm.Cells.Count);
                room.FindControl<CompanionHeroCard>("HeroZone")!.ViewModel!.SwitchCommand.Execute(null);
                Assert.True(vm.IsExpanded);
                window.UpdateLayout();
                Assert.Equal(6, shop.FindControl<ItemsControl>("CellHost")!.ItemCount);
                vm.IsExpanded = false;
                room.FindControl<AwarenessPrivacyView>("AwarenessZone")!.ViewModel!.FineTuningCommand.Execute(null);
                Assert.True(vm.IsExpanded);
                previous = vm;
            }
        }
        finally { window.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task PreviewPermissionsKeepWpfDefaultsAndNeverWriteOrForget() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var window = Create();
        var previousBrain = ConditioningControlPanel.Avalonia.App.Brain;
        var dir = Directory.CreateTempSubdirectory("ccp-preview-memory-").FullName;
        using var store = new MemoryStore(Path.Combine(dir, "memory.json"));
        using var brain = new CompanionBrain(new ConditioningControlPanel.Services.AiService("http://127.0.0.1:9"),
            memory: store, store: new CompanionSessionStore(Path.Combine(dir, "session.json"), Path.Combine(dir, "legacy.json")));
        brain.Session.Append(TurnKind.UserChat, "keep this preview sentinel");
        ConditioningControlPanel.Avalonia.App.Brain = brain;
        try
        {
            window.Show();
            var grid = window.RoomView.FindControl<AiPermissionsGrid>("PermissionsZone")!;
            var before = Newtonsoft.Json.JsonConvert.SerializeObject(ConditioningControlPanel.CoreSettings.Current);
            var effects = grid.FindControl<CheckBox>("ChkCapEffects")!;
            var memory = grid.FindControl<CheckBox>("ChkChatMemoryEnabled")!;
            var cap = grid.FindControl<Slider>("SliderMaxHapticIntensity")!;
            Assert.True(grid.IsEnabled);
            Assert.False(effects.IsChecked);
            Assert.True(memory.IsChecked);
            Assert.Equal(0.6, cap.Value);
            Assert.Equal("60%", grid.FindControl<TextBlock>("TxtMaxHapticIntensity")!.Text);
            foreach (var tag in new[] { "Flash", "Video", "Audio", "Bubbles", "Subliminal", "Overlay", "LockCard", "Bounce", "Haptic", "GetBackToMe" })
            {
                var box = grid.FindControl<CheckBox>("ChkAllow" + tag)!;
                Assert.False(box.IsChecked);
                box.IsChecked = true;
            }
            effects.IsChecked = true;
            memory.IsChecked = false;
            cap.Value = 0.2;
            Assert.False(grid.FindControl<Border>("EffectPermsPanel")!.IsVisible); // WPF shim does nothing
            Assert.Equal("60%", grid.FindControl<TextBlock>("TxtMaxHapticIntensity")!.Text);
            foreach (var name in new[] { "BtnClearChatMemory", "BtnLabEffectsSetupLocal", "BtnEffectsLockCta" })
                grid.FindControl<Button>(name)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            // A settings-replacement refresh and a state-strip click must not repaint the local controls.
            grid.SyncFromSettings();
            window.ShowVariant("disabled");
            Assert.True(effects.IsChecked);
            Assert.False(memory.IsChecked);
            Assert.Equal(0.2, cap.Value);
            Assert.Equal(before, Newtonsoft.Json.JsonConvert.SerializeObject(ConditioningControlPanel.CoreSettings.Current));
            Assert.Equal("keep this preview sentinel", Assert.Single(brain.Session.Turns).Text);
            Assert.Empty(window.OwnedWindows); // no memory confirmation or setup wizard
            // Live grids still restore the saved setting; the preview flag is per instance.
            var live = new AiPermissionsGrid();
            Assert.Equal(ConditioningControlPanel.CoreSettings.Current.CompanionPrompt!.ChatMemoryEnabled,
                live.FindControl<CheckBox>("ChkChatMemoryEnabled")!.IsChecked);
        }
        finally
        {
            ConditioningControlPanel.Avalonia.App.Brain = previousBrain;
            window.Close();
        }
        return Task.CompletedTask;
    });

}
