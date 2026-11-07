using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.AudioPlayer;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public class Sync7ShellTests
{
    [Fact]
    public void PlaylistStartupSlotUsesTheSharedContract()
    {
        var slot = typeof(AvApp).GetProperty("AudioPlaylist");
        Assert.NotNull(slot);
        Assert.Equal(typeof(IAudioPlaylistService), slot.PropertyType);
        Assert.False(slot.SetMethod!.IsPublic);
        Assert.Null(slot.GetValue(null));
    }

    [Fact]
    public Task PresetsExplainTheirScope() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var tab = new PresetsTabView();
        var line = tab.FindControl<TextBlock>("TxtPresetScope");
        Assert.NotNull(line);
        Assert.True(line.IsVisible);
        Assert.Equal(Loc.Get("sd_presets_scope"), line.Text);
        Assert.Equal(global::Avalonia.Media.TextWrapping.Wrap, line.TextWrapping);
        return Task.CompletedTask;
    });
}
