using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion.Runtime;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ShellAssetsCompletionTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    [Fact]
    public void ThumbnailUsesPortableDecoderOnlyOffWindows()
    {
        string? requested = null;
        var missing = Path.Combine(Path.GetTempPath(), "ccp-missing-" + Guid.NewGuid().ToString("N"), "clip.mp4");
        Assert.Null(global::ConditioningControlPanel.Avalonia.Platform.ShellVideoThumbnail.Load(missing, path =>
        {
            requested = path;
            return null;
        }));
        Assert.Equal(OperatingSystem.IsWindows() ? null : missing, requested);
    }

    [Fact]
    public Task VoiceRowsAffectSavedPresetCountAndRespectDisabledAndRemovedIds() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        using var mods = new CoreModsSnapshot();
        var previous = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        CoreMods.ActiveModIdProvider = () => "fin4-test";
        CoreMods.ActiveModPackageProvider = () => null;
        CoreMods.PhrasesProvider = _ => Array.Empty<string>();
        var relative = Path.Combine("Resources", "sounds", "companion_audio", "mods", "fin4-test", "flashes_audio");
        var folder = Path.Combine(ContentLocator.ContentRoot, relative);
        Directory.CreateDirectory(folder);
        foreach (var name in new[] { "a.mp3", "b.wav", "c.ogg" }) File.WriteAllBytes(Path.Combine(folder, name), Array.Empty<byte>());
        var s = service.Current;
        s.CustomCompanionPhrases.Clear();
        s.DisabledPhraseIds.Clear(); s.RemovedPhraseIds.Clear();
        s.DisabledPhraseIds.Add("VoiceLine:0"); s.RemovedPhraseIds.Add("VoiceLine:c");
        try
        {
            var rows = CompanionPhraseEditorDialog.LoadPhrases().Where(p => p.Category == "VoiceLine").ToList();
            Assert.Equal(new[] { "a", "b" }, rows.Select(p => p.Text));
            Assert.False(rows[0].IsEnabled);
            Assert.Equal(folder, rows[1].AudioFolder);
            Assert.Equal("b.wav", rows[1].AudioFileName);
            var cell = new WorkshopTriggersCell();
            cell.UpdatePhraseCountDisplay();
            const int active = 1;   // b only: a is disabled, c is removed
            Assert.Equal(Loc.GetF("label_0_active_2", active), cell.FindControl<TextBlock>("TxtPhraseCount")!.Text);
            s.PhrasePresets.Clear();
            cell.AskText = (_, _, _) => Task.FromResult<string?>("Voice preset");
            await cell.SavePhrasePresetAsync();
            Assert.Equal(active, Assert.Single(s.PhrasePresets).ActivePhraseCount);
        }
        finally
        {
            Directory.Delete(Path.Combine(ContentLocator.ContentRoot, "Resources", "sounds", "companion_audio", "mods", "fin4-test"), true);
            service.SealForReset(); CoreSettings.ServiceProvider = previous;
        }
    });

    [Fact]
    public Task NestedWheelMovesOuterEvenWhenInnerCanScroll() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var inner = new ScrollViewer { Height = 100, Content = new Border { Height = 600 } };
        ParentWheel.Attach(inner);
        var outer = new ScrollViewer { Content = new StackPanel { Children = { inner, new Border { Height = 1000 } } } };
        var window = new Window { Width = 400, Height = 300, Content = outer };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var wheel = new PointerWheelEventArgs(inner, new Pointer(1, PointerType.Mouse, true), window, default, 0, default,
                KeyModifiers.None, new Vector(0, -1));
            inner.RaiseEvent(wheel);
            Assert.True(wheel.Handled);
            Assert.Equal(120, outer.Offset.Y);
            Assert.Equal(0, inner.Offset.Y);
        }
        finally { window.Close(); }
        return Task.CompletedTask;
    });
}
