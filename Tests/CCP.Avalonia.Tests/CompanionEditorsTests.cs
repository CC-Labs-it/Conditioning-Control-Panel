using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Deeper;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>rows-companion-editors: the prompt editor's save contract and X prompt (WPF
/// CompanionPromptEditorDialog.xaml.cs SaveSettings/OnClosing) and the attention style editor's
/// Test target (WPF BtnTest_Click + FloatingText), each through the real dialog.</summary>
public sealed class CompanionEditorsTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        LocalizationManager.Instance.SetLanguage("en");
    }

    private static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static async Task Until(Func<bool> done)
    {
        // Bounded by time, not turns: turn counts pass alone and fail under gate load (PLAYBOOK P73).
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!done() && DateTime.UtcNow < deadline) { await Task.Yield(); Dispatcher.UIThread.RunJobs(); }
        Assert.True(done());
    }

    private static void Answer(Window owner, string label)
    {
        var dlg = owner.OwnedWindows.OfType<UnsavedChangesDialog>().Single();
        Click(dlg.GetLogicalDescendants().OfType<Button>().Single(b => (b.Content as TextBlock)?.Text == label));
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public Task PromptEditor_persists_only_edits_and_unticking_drops_the_community_prompt() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        try
        {
            var s = CoreSettings.Current;
            s.ActiveCommunityPromptId = "community-x";
            s.CompanionPrompt.UseCustomPrompt = true;
            s.CompanionPrompt.Personality = "";
            s.CompanionPrompt.ExplicitReaction = "";
            var dlg = new CompanionPromptEditorDialog();
            var shown = dlg.FindControl<TextBox>("TxtExplicitReaction")!.Text;
            Assert.False(string.IsNullOrWhiteSpace(shown));                       // seeded from the persona/stock text

            dlg.FindControl<TextBox>("TxtPersonality")!.Text = "mine";
            dlg.FindControl<CheckBox>("ChkUseCustom")!.IsChecked = false;
            Click(dlg.FindControl<Button>("BtnSave")!);

            Assert.Equal("mine", s.CompanionPrompt.Personality);
            Assert.Equal("", s.CompanionPrompt.ExplicitReaction);                  // untouched box follows the default
            Assert.False(s.CompanionPrompt.UseCustomPrompt);
            Assert.Null(s.ActiveCommunityPromptId);                                // WPF ClearCustomPromptOverride
        }
        finally { CoreSettings.ServiceProvider = null; }
        return Task.CompletedTask;
    });

    [Fact]
    public Task PromptEditor_X_with_edits_asks_Save_Discard_Cancel() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var owner = new Window();
        owner.Show();
        try
        {
            var s = CoreSettings.Current;
            s.CompanionPrompt.UseCustomPrompt = true;
            s.CompanionPrompt.Personality = "";
            var dlg = new CompanionPromptEditorDialog();
            _ = dlg.ShowDialogSafe(owner);
            Dispatcher.UIThread.RunJobs();
            dlg.FindControl<TextBox>("TxtPersonality")!.Text = "edited";

            dlg.Close();                                                           // the X
            await Until(() => dlg.OwnedWindows.OfType<UnsavedChangesDialog>().Any());
            Answer(dlg, Loc.Get("btn_cancel"));
            Assert.True(dlg.IsVisible);                                            // Cancel keeps it open

            dlg.Close();
            await Until(() => dlg.OwnedWindows.OfType<UnsavedChangesDialog>().Any());
            Answer(dlg, Loc.Get("btn_save"));
            await Until(() => !dlg.IsVisible);
            Assert.Equal("edited", s.CompanionPrompt.Personality);

            var again = new CompanionPromptEditorDialog();
            _ = again.ShowDialogSafe(owner);
            Dispatcher.UIThread.RunJobs();
            again.FindControl<TextBox>("TxtPersonality")!.Text = "thrown away";
            again.Close();
            await Until(() => again.OwnedWindows.OfType<UnsavedChangesDialog>().Any());
            Answer(again, Loc.Get("btn_discard"));
            await Until(() => !again.IsVisible);
            Assert.Equal("edited", s.CompanionPrompt.Personality);

            var clean = new CompanionPromptEditorDialog();                          // nothing edited: no prompt
            _ = clean.ShowDialogSafe(owner);
            Dispatcher.UIThread.RunJobs();
            clean.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.False(clean.IsVisible);
        }
        finally
        {
            owner.Close();
            CoreSettings.ServiceProvider = null;
        }
    });

    [Fact]
    public Task PromptEditor_community_badge_uses_installed_name_or_localized_unknown() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var id = "badge-" + Guid.NewGuid().ToString("N");
        var folder = System.IO.Path.Combine(CorePaths.UserData, ConditioningControlPanel.Services.Companion.InstalledCommunityPrompts.FolderName);
        var path = System.IO.Path.Combine(folder, id + ".json");
        System.IO.Directory.CreateDirectory(folder);
        try
        {
            CoreSettings.Current.ActiveCommunityPromptId = id;
            System.IO.File.WriteAllText(path, "{\"Id\":\"" + id + "\",\"Name\":\"My community persona\"}");
            var installed = new CompanionPromptEditorDialog();
            var badge = installed.FindControl<TextBlock>("TxtActivePromptName")!;
            Assert.Equal("My community persona", badge.Text);
            Assert.Equal(Color.FromRgb(147, 112, 219), ((SolidColorBrush)badge.Foreground!).Color);
            LocalizationManager.Instance.SetLanguage("de");
            Assert.Equal("My community persona", badge.Text); // literal names must survive language changes
            installed.Close();

            System.IO.File.Delete(path);
            var missing = new CompanionPromptEditorDialog();
            badge = missing.FindControl<TextBlock>("TxtActivePromptName")!;
            Assert.Equal(Loc.Get("label_unknown_prompt"), badge.Text);
            Assert.Equal(Color.FromRgb(255, 107, 107), ((SolidColorBrush)badge.Foreground!).Color);
            LocalizationManager.Instance.SetLanguage("en");
            Assert.Equal(Loc.Get("label_unknown_prompt"), badge.Text);
            missing.Close();

            System.IO.File.WriteAllText(path, "broken json");
            var corrupt = new CompanionPromptEditorDialog();
            Assert.Equal(Loc.Get("label_unknown_prompt"), corrupt.FindControl<TextBlock>("TxtActivePromptName")!.Text);
            corrupt.Close();
        }
        finally
        {
            System.IO.File.Delete(path);
            CoreSettings.ServiceProvider = null;
            LocalizationManager.Instance.SetLanguage("en");
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task PromptEditor_is_reachable_from_make_her_yours_links() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            shell.CompleteTabTransition();
            // Park subsequent entrances so pointer success cannot depend on runner speed.
            shell.TabClock = new SteppedTime();
            shell.ShowTab("companion");
            // WPF v2: Companion > Personality adopts Make-her-yours (Pages/PersonalityPage.xaml.cs:43).
            shell.Named<SectionTabStrip>("SectionStrip")!.PillFor("personality")!
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            shell.CompleteTabTransition();
            Assert.Null(shell.EnteringTab);
            Dispatcher.UIThread.RunJobs();
            var view = shell.GetLogicalDescendants()
                .OfType<ConditioningControlPanel.Avalonia.Views.Controls.Companion.MakeHerYoursView>().Single();
            // Adjust/traits are dormant Train 3 controls in WPF; drive the two live links.
            foreach (var command in new[] { view.ViewModel.ViewCompiledPromptCommand, view.ViewModel.ForkPromptCommand })
            {
                var button = view.GetLogicalDescendants().OfType<Button>().Single(b => b.Command == command);
                button.BringIntoView();
                // Scroll/layout and the tab entrance must be in the compositor's input tree.
                ContentHitTestTests.RenderLiveTree(shell);
                var at = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), shell)!.Value;
                shell.MouseDown(at, global::Avalonia.Input.MouseButton.Left);
                shell.MouseUp(at, global::Avalonia.Input.MouseButton.Left);
                Dispatcher.UIThread.RunJobs();
                await Until(() => shell.OwnedWindows.OfType<CompanionPromptEditorDialog>().Any());
                var editor = shell.OwnedWindows.OfType<CompanionPromptEditorDialog>().Single();
                Click(editor.FindControl<Button>("BtnCancel")!);
                await Until(() => !editor.IsVisible);
            }
        }
        finally { shell.Close(); CoreSettings.ServiceProvider = null; }
    });

    private sealed class SteppedClock : TimeProvider
    {
        public long Now;
        public override long GetTimestamp() => Now;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    }

    [Fact]
    public Task AttentionStyle_Test_spawns_the_unsaved_style_bouncing_until_panic() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var clock = new SteppedClock();
        AttentionTestTarget.Clock = clock;
        var oldPlay = CoreAudio.PlayOneShotProvider;
        var played = new System.Collections.Generic.List<string>();
        CoreAudio.PlayOneShotProvider = (p, _, _, _, done) => { played.Add(p); done?.Invoke(); };
        var card = new VideoFeatureControl();
        var host = new Window { Content = card };
        host.Show();
        try
        {
            var s = CoreSettings.Current;
            s.AttentionColor1 = "#FF64C8";
            s.AttentionFloatingText = false;
            Click(card.FindControl<Button>("BtnAttentionStyle")!);
            await Until(() => host.OwnedWindows.OfType<AttentionTargetEditorDialog>().Any());
            var dlg = host.OwnedWindows.OfType<AttentionTargetEditorDialog>().Single();

            Click(dlg.FindControl<Button>("PresetBlue")!);
            Click(dlg.FindControl<Button>("BtnTest")!);
            var t = Assert.Single(AttentionTestTarget.Open);
            Assert.True(t.Window.IsVisible);
            var pill = (Border)t.Window.Content!;
            var stop = ((LinearGradientBrush)pill.Background!).GradientStops[0].Color;
            Assert.Equal(Color.Parse("#3498DB"), stop);                            // the dialog's unsaved preset
            Assert.Equal("#FF64C8", s.AttentionColor1);                             // restored: nothing saved

            var (x, y) = (t.X, t.Y);
            clock.Now += TimeSpan.TicksPerSecond / 10;
            t.Tick();
            var moved = Math.Sqrt((t.X - x) * (t.X - x) + (t.Y - y) * (t.Y - y));
            Assert.InRange(moved, 1, 18.76);                                        // 187.5 DIP/s for 0.1 s (less at a wall)

            t.Hit();                                                                // WPF: a hit pops
            Assert.Contains(played, p => System.IO.Path.GetFileName(p).StartsWith("Pop"));

            PanicSurfaces.All.Single(x => x.Id == "attention-test").Stop(null);   // the registered panic stop
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(AttentionTestTarget.Open);
            Assert.False(t.Window.IsVisible);
            dlg.Close();
        }
        finally
        {
            AttentionTestTarget.CloseAll();
            AttentionTestTarget.Clock = TimeProvider.System;
            CoreAudio.PlayOneShotProvider = oldPlay;
            host.Close();
            CoreSettings.ServiceProvider = null;
        }
    });
}
