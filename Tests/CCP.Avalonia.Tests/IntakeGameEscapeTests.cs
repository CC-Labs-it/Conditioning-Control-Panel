using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Skia;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Newtonsoft.Json.Linq;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class IntakeGameEscapeTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task FirstEscapePausesReadyIntakeAndSecondPanics(bool ready) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        IntakeTestApp.Ensure();
        var settings = CoreSettings.Current;
        var saved = (settings.PanicKeyEnabled, settings.PanicKey, settings.PanicOverridesAll);
        Assert.False(CoreEngine.IsRunning);
        (settings.PanicKeyEnabled, settings.PanicKey, settings.PanicOverridesAll) = (true, "Escape", true);
        var shell = new MainShellWindow();
        var host = new IntakeHostWindow();
        var frames = new List<JObject>();
        host.Posted += json => frames.Add(JObject.Parse(json));
        try
        {
            host.Show();
            host.Activate();
            // Headless has no OS activation callback; deliver the real WindowBase notification.
            typeof(global::Avalonia.Controls.WindowBase).GetMethod("HandleActivated",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(host, null);
            Assert.True(host.IsActive);
            if (ready) host.HandleMessage("{\"type\":\"ready\"}");
            frames.Clear();
            var now = new DateTime(2026, 10, 22, 12, 0, 0);
            shell.HandlePanicKeyPress(now);
            Assert.True(host.IsVisible);
            Assert.Equal(ready ? 1 : 0, frames.FindAll(f => (string?)f["type"] == "kept-escape").Count);
            shell.HandlePanicKeyPress(now.AddSeconds(1));
            Assert.False(host.IsVisible);
        }
        finally
        {
            host.Close();
            shell.Close();
            (settings.PanicKeyEnabled, settings.PanicKey, settings.PanicOverridesAll) = saved;
        }
        return Task.CompletedTask;
    });
}

internal static class IntakeTestApp
{
    internal static void Ensure()
    {
        if (Application.Current != null) return;
        var fonts = ConditioningControlPanel.Avalonia.Platform.AppFonts.Options(false);
        var mappings = new Dictionary<string, global::Avalonia.Media.FontFamily>(fonts.FontFamilyMappings!);
        mappings["Segoe UI"] = mappings["Segoe UI Black"] = new global::Avalonia.Media.FontFamily(
            ConditioningControlPanel.Avalonia.Platform.AppFonts.Inter);
        fonts.FontFamilyMappings = mappings;
        AppBuilder.Configure<AvApp>().UseSkia().WithInterFont().With(fonts)
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }
}
