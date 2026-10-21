using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class LauncherPresetDropTests
{
    [Theory]
    [InlineData("download.preset.json", false)]
    [InlineData("download.preset (1).json", false)]
    [InlineData("renamed.json", false)]
    [InlineData("invalid.preset.json", true)]
    public Task FileDroppedOnLauncherOpensPanelAndImportsThroughItsExistingPath(string fileName, bool invalid) =>
        AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var root = Directory.CreateTempSubdirectory("ccp-launcher-preset-").FullName;
        var previous = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var settings = service.Current;
        settings.Welcomed = settings.HasAcceptedAgeVerification = true;
        settings.MotionLevel = MotionLevel.Off;
        settings.SeenFeatureIntros.AddRange(FeatureIntros.All.Keys);
        var originalPresets = settings.UserPresets.ToArray();
        var id = Guid.NewGuid().ToString();
        var file = Path.Combine(root, fileName);
        var preset = Preset.FromSettings(settings, "Launcher catalogue drop");
        preset.Id = id;
        File.WriteAllText(file, invalid ? "not a preset" : new PresetFileService().SerializePreset(preset));
        var shell = new MainShellWindow();
        LauncherWindow? launcher = null;
        try
        {
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.True(LauncherWindow.BackToLauncher(shell));
            launcher = LauncherWindow.Instance!;
            Assert.False(shell.IsVisible);
            Assert.True(launcher.IsVisible);
            Assert.True(DragDrop.GetAllowDrop(launcher));
            var storageFile = await launcher.StorageProvider.TryGetFileFromPathAsync(file);
            Assert.NotNull(storageFile);
            var data = new DataTransfer();
            data.Add(DataTransferItem.CreateFile(storageFile!));
            launcher.DragDrop(new Point(400, 300), RawDragEventType.DragEnter, data, DragDropEffects.Copy, RawInputModifiers.None);
            // Registered after Launcher_DragOver, so it reads the effect that handler chose.
            var over = (DragDropEffects)(-1);
            launcher.AddHandler(DragDrop.DragOverEvent, (_, e) => over = e.DragEffects, handledEventsToo: true);
            var text = new DataTransfer();
            text.Add(DataTransferItem.CreateText("not a file"));
            launcher.DragDrop(new Point(400, 300), RawDragEventType.DragOver, text, DragDropEffects.Copy, RawInputModifiers.None);
            Assert.Equal(DragDropEffects.None, over);
            launcher.DragDrop(new Point(400, 300), RawDragEventType.DragOver, data, DragDropEffects.Copy, RawInputModifiers.None);
            Assert.Equal(DragDropEffects.Copy, over);
            launcher.DragDrop(new Point(400, 300), RawDragEventType.Drop, data, DragDropEffects.Copy, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.True(shell.IsVisible);
            Assert.False(launcher.IsVisible);
            var status = shell.Named<global::ConditioningControlPanel.Avalonia.Views.Tabs.PresetsTabView>("PresetsTab")!
                .FindControl<TextBlock>("DropZoneStatus")!.Text;
            if (invalid)
            {
                Assert.Equal(originalPresets.Length, settings.UserPresets.Count);
                Assert.StartsWith("Invalid:", status);
            }
            else
            {
                Assert.Contains(settings.UserPresets, p => p.Id == id && p.Name == "Launcher catalogue drop");
                Assert.Equal("Preset imported: Launcher catalogue drop", status);
            }
            Assert.True(File.Exists(file));
        }
        finally
        {
            launcher?.Close();
            foreach (var owned in shell.OwnedWindows.ToArray()) owned.Close();
            shell.Close();
            settings.UserPresets.Clear(); settings.UserPresets.AddRange(originalPresets);
            service.SaveImmediate(); service.SealForReset();
            CoreSettings.ServiceProvider = previous;
            foreach (var copied in Directory.Exists(PresetFileService.CustomPresetsFolder)
                ? Directory.GetFiles(PresetFileService.CustomPresetsFolder, id + "*") : Array.Empty<string>()) File.Delete(copied);
            Directory.Delete(root, true);
        }
    });
}
