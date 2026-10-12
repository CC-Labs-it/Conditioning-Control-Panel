using System;
using Avalonia;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// The GPU budget and the Win32 presentation switches (Platform/RenderBudget.cs, from the parity
/// trunk's smoothness lane f5d0ef3ec). Pure rules; the app wires them in Program.BuildAvaloniaApp.
/// </summary>
public sealed class RenderBudgetTests
{
    [Fact]
    public void Gpu_cache_holds_the_whole_Home_working_set_with_headroom()
    {
        Assert.True(RenderBudget.GpuResourceCacheBytes >= RenderBudget.HomeWorkingSetBytes * 3 / 2,
            $"cache {RenderBudget.GpuResourceCacheBytes} vs working set {RenderBudget.HomeWorkingSetBytes}");
        // Avalonia's default (~28 MB) is below the working set: that is the bug being pinned.
        Assert.True(1024L * 600 * 4 * 12 < RenderBudget.HomeWorkingSetBytes);
    }

    [Fact]
    public void Presentation_switches_parse_in_priority_order_and_ignore_junk()
    {
        static Win32CompositionMode? Map(string s) => s switch
        {
            "winui" => Win32CompositionMode.WinUIComposition,
            "swapchain" => Win32CompositionMode.LowLatencyDxgiSwapChain,
            "redirection" => Win32CompositionMode.RedirectionSurface,
            _ => null,
        };
        Assert.Null(RenderBudget.ParseModes<Win32CompositionMode>(null, Map));
        Assert.Null(RenderBudget.ParseModes<Win32CompositionMode>("  ", Map));
        Assert.Null(RenderBudget.ParseModes<Win32CompositionMode>("nonsense", Map));
        var modes = RenderBudget.ParseModes<Win32CompositionMode>(" SwapChain, redirection ,swapchain", Map)!;
        Assert.Equal(new[] { Win32CompositionMode.LowLatencyDxgiSwapChain, Win32CompositionMode.RedirectionSurface }, modes);
    }

    [Fact]
    public void Adapter_choice_defaults_to_the_primary_and_honours_index_or_name()
    {
        var names = new string?[] { "Intel(R) Graphics", "NVIDIA GeForce RTX 5080" };
        Assert.Equal(0, RenderBudget.ChooseAdapter(names, null));
        Assert.Equal(0, RenderBudget.ChooseAdapter(names, ""));
        Assert.Equal(1, RenderBudget.ChooseAdapter(names, "1"));
        Assert.Equal(0, RenderBudget.ChooseAdapter(names, "7"));      // out of range: primary
        Assert.Equal(1, RenderBudget.ChooseAdapter(names, "nvidia"));
        Assert.Equal(0, RenderBudget.ChooseAdapter(names, "amd"));    // unknown name: primary
        Assert.Equal(0, RenderBudget.ChooseAdapter(Array.Empty<string?>(), "1"));
    }
}
