using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ChaosAttentionRestackReviewTests
{
    [Fact]
    public async Task Video_click_raises_a_live_attention_surface_after_delay()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var overlay = MandatoryVideoOverlay.Instance;
            var field = typeof(MandatoryVideoOverlay).GetField("_surfaces", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var surfaces = (List<MandatoryVideoOverlay.Surface>)field.GetValue(overlay)!;
            var attention = new Window();
            var layer = new Canvas();
            layer.Children.Add(new Border());
            var surface = new MandatoryVideoOverlay.Surface(new Window(), new Image(), new Border(), new Image(), layer, new Border(), new TextBlock(), null!) { AttentionWindow = attention };
            var oldSchedule = MandatoryVideoOverlay.ScheduleGameRestack;
            var oldRaise = MandatoryVideoOverlay.RaiseAttentionWindow;
            Action? scheduled = null;
            var raises = new List<Window>();
            try
            {
                attention.Show();
                surfaces.Add(surface);
                MandatoryVideoOverlay.ScheduleGameRestack = (action, delay) => { Assert.Equal(TimeSpan.FromMilliseconds(50), delay); scheduled = action; };
                MandatoryVideoOverlay.RaiseAttentionWindow = raises.Add;
                MandatoryVideoOverlay.RestackOnVideoClick();
                Assert.Empty(raises);
                Assert.NotNull(scheduled);
                scheduled();
                Assert.Same(attention, Assert.Single(raises));
            }
            finally
            {
                surfaces.Remove(surface);
                attention.Close();
                surface.Window.Close();
                MandatoryVideoOverlay.ScheduleGameRestack = oldSchedule;
                MandatoryVideoOverlay.RaiseAttentionWindow = oldRaise;
            }
            return Task.CompletedTask;
        });
    }
}
