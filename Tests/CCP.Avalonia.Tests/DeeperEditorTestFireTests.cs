using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Deeper;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models.Deeper;
using Xunit;
namespace CCP.Avalonia.Tests;
[Collection(RunsAloneCollection.Name)]
public sealed class DeeperEditorTestFireTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }
    private static void Click(Button b) { b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }
    [Fact]
    public void HapticFeedbackExistsInAllTenLanguages()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(SourceFile())!, "../../CCP.Core/Localization/Languages"));
        var files = Directory.GetFiles(root, "*.json");
        Assert.Equal(10, files.Length);
        foreach (var file in files)
        {
            using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(file));
            foreach (var suffix in new[] { "no_device", "row_off", "premium" })
                Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("deeper_editor_haptic_test_" + suffix).GetString()), file);
        }
    }
    private static string SourceFile([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;
    [Fact]
    public Task HapticTestWithoutADeviceSaysSoInTheStrip() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var old = CoreHaptics.Service;
        var editor = new DeeperEditorWindow();   // the sample project carries one haptic event
        try
        {
            CoreHaptics.Service = null;
            editor.Show();
            Dispatcher.UIThread.RunJobs();
            var track = editor.GetType().GetField("_enhancement", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .GetValue(editor) is Enhancement e ? e.HapticTracks[0] : throw new InvalidOperationException();
            editor.SelectHaptic(track, track.Events[0]);
            Click(editor.BtnTestHaptic);
            Assert.True(editor.TxtValidationSummary.IsVisible);
            Assert.False(editor.TxtValidationClean.IsVisible);
            Assert.Equal(Loc.Get("deeper_editor_haptic_test_no_device"), editor.TxtValidationSummary.Text);
        }
        finally
        {
            editor.Close();
            CoreHaptics.Service = old;
        }
        return Task.CompletedTask;
    });
    [Theory]
    [InlineData(false, true, "deeper_editor_haptic_test_row_off")]
    [InlineData(true, false, "deeper_editor_haptic_test_premium")]
    [InlineData(true, true, null)]
    public Task HapticTestHonoursRoutingAndPremium(bool routing, bool premium, string? message) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var settings = CoreSettings.Current.Haptics;
        settings.EnsureV2Migrated();
        settings.Enabled = false; // a manual preview waives the master toggle, as on WPF
        settings.V2.Provider("mock").Enabled = true;
        settings.V2.Provider("buttplug").Enabled = false;
        settings.V2.Provider("lovense").Enabled = false;
        settings.V2.Rule(ConditioningControlPanel.Services.Haptics.Core.HapticLayer.Pattern).Enabled = routing;
        CoreEntitlement.HasPremiumProvider = () => premium;
        var toasts = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var oldToast = ConditioningControlPanel.Services.Haptics.Core.MockProviderV2.Toast;
        ConditioningControlPanel.Services.Haptics.Core.MockProviderV2.Toast = toasts.Enqueue;
        using var haptics = new ConditioningControlPanel.Services.HapticService(settings);
        CoreHaptics.Service = haptics;
        var editor = new DeeperEditorWindow();
        try
        {
            Assert.True(await haptics.ConnectAsync());
            editor.Show(); Dispatcher.UIThread.RunJobs();
            var enhancement = (Enhancement)editor.GetType().GetField("_enhancement", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(editor)!;
            var track = enhancement.HapticTracks[0];
            editor.SelectHaptic(track, track.Events[0]);
            Click(editor.BtnTestHaptic);
            if (message != null) Assert.Equal(Loc.Get(message), editor.TxtValidationSummary.Text);
            else
            {
                var deadline = System.Diagnostics.Stopwatch.StartNew();
                while (!toasts.Any(t => t.Contains('%')) && deadline.Elapsed < TimeSpan.FromSeconds(5))
                    await Task.Delay(20);
                Assert.Contains(toasts, t => t.Contains('%'));
            }
        }
        finally
        {
            editor.ForceClose(); haptics.PanicStop(); CoreHaptics.Service = null;
            ConditioningControlPanel.Services.Haptics.Core.MockProviderV2.Toast = oldToast;
        }
    });
}
