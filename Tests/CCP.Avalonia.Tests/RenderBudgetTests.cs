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

    private static readonly RenderBudget.DxgiAdapter Warp = new("Microsoft Basic Render Driver", 0x1414, 0x8C, 2);
    private static readonly RenderBudget.DxgiAdapter Rtx = new("NVIDIA GeForce RTX 5080", 0x10DE, 0x2C02, 0);

    [Fact]
    public void Warp_only_renders_in_software()
    {
        Assert.Equal(new[] { Win32RenderingMode.Software }, RenderBudget.ChooseRendering(new[] { Warp }, null));
        // Either signal alone is enough: the WARP ids without the flag, or the software flag on another id.
        Assert.Equal(new[] { Win32RenderingMode.Software },
            RenderBudget.ChooseRendering(new[] { Warp with { Flags = 0 }, new RenderBudget.DxgiAdapter("x", 1, 2, 2) }, null));
    }

    [Fact]
    public void A_real_gpu_keeps_the_default_even_beside_warp()
    {
        Assert.Null(RenderBudget.ChooseRendering(new[] { Rtx, Warp }, null));
        Assert.Null(RenderBudget.ChooseRendering(new[] { Rtx }, null));
        Assert.Null(RenderBudget.ChooseRendering(new[] { new RenderBudget.DxgiAdapter("Remote", 0x1414, 0x8D, 0) }, null));
        Assert.Null(RenderBudget.ChooseRendering(Array.Empty<RenderBudget.DxgiAdapter>(), null));   // enumeration failed
    }

    [Fact]
    public void The_env_override_wins_over_the_adapter_rule()
    {
        var angle = new[] { Win32RenderingMode.AngleEgl };
        Assert.Same(angle, RenderBudget.ChooseRendering(new[] { Warp }, angle));
        var soft = new[] { Win32RenderingMode.Software };
        Assert.Same(soft, RenderBudget.ChooseRendering(new[] { Rtx }, soft));
    }

    // The enumeration loop DxgiAdapters.List drives (EnumAdapters1 + GetDesc1 folded into one HRESULT).
    private static RenderBudget.ReadAdapter Reads(params (int Hr, RenderBudget.DxgiAdapter A)[] steps) =>
        (uint i, out RenderBudget.DxgiAdapter a) => { var s = steps[i]; a = s.A; return s.Hr; };

    private const int NotFound = RenderBudget.DxgiErrorNotFound, EFail = unchecked((int)0x80004005);

    [Fact]
    public void Enumeration_stops_at_not_found_and_keeps_every_adapter()
    {
        Assert.Equal(new[] { Rtx, Warp }, RenderBudget.CollectAdapters(Reads((0, Rtx), (0, Warp), (NotFound, default))));
        Assert.Equal(new[] { Warp }, RenderBudget.CollectAdapters(Reads((0, Warp), (NotFound, default))));
        Assert.Empty(RenderBudget.CollectAdapters(Reads((NotFound, default))));
    }

    [Fact]
    public void A_partial_enumeration_failure_keeps_the_default_renderer()
    {
        // The GPU at index 0 fails to read and WARP reads fine: without the clear this was [WARP] -> Software.
        var gpuUnread = RenderBudget.CollectAdapters(Reads((EFail, default), (0, Warp), (NotFound, default)));
        Assert.Empty(gpuUnread);
        Assert.Null(RenderBudget.ChooseRendering(gpuUnread, null));
        // A failure after WARP was read, and an exception, drop the list too.
        Assert.Empty(RenderBudget.CollectAdapters(Reads((0, Warp), (EFail, default))));
        Assert.Empty(RenderBudget.CollectAdapters((uint i, out RenderBudget.DxgiAdapter a) =>
        {
            if (i == 1) throw new InvalidOperationException("driver");
            a = Warp; return 0;
        }));
    }

    private static Win32PlatformOptions Startup(string? rendering, params RenderBudget.DxgiAdapter[] adapters)
    {
        bool listed = false;
        var o = global::ConditioningControlPanel.Avalonia.Program.Win32Options(
            name => name == "CCP_WIN32_RENDERING" ? rendering : null,
            () => { listed = true; return adapters; });
        Assert.Equal(rendering == null, listed);   // the override skips the enumeration
        return o;
    }

    [Fact]
    public void Startup_applies_the_chosen_rendering_mode()
    {
        var avaloniaDefault = new Win32PlatformOptions().RenderingMode;
        Assert.Equal(new[] { Win32RenderingMode.Software }, Startup(null, Warp).RenderingMode);
        Assert.Equal(avaloniaDefault, Startup(null, Rtx, Warp).RenderingMode);
        Assert.Equal(avaloniaDefault, Startup(null).RenderingMode);   // enumeration failed or not Windows
        Assert.Equal(new[] { Win32RenderingMode.AngleEgl }, Startup("angle", Warp).RenderingMode);
    }

    [Fact]
    public void Windows_reads_real_adapters_and_a_warp_only_machine_renders_in_software()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Skip("DXGI exists only on Windows."); return; }   // return: CA1416 flow
        var real = DxgiAdapters.List();
        Assert.NotEmpty(real);   // DXGI always lists at least the Basic Render Driver
        foreach (var a in real)
        {
            Assert.False(string.IsNullOrWhiteSpace(a.Name));
            // DXGI_ADAPTER_DESC1.Flags holds only REMOTE (1) and SOFTWARE (2): a misread layout lands LUID or padding
            // bytes here. The Basic Display Adapter shares the Basic Render Driver's 0x1414/0x8C ids without the
            // SOFTWARE flag (windows-latest runners read 0 there), so the ids alone cannot demand the flag;
            // IsSoftware covers both by the ids.
            Assert.Equal(0u, a.Flags & ~3u);
            Assert.NotEqual(0u, a.VendorId);
            if ((a.Flags & 2) != 0) Assert.True(a.IsSoftware);
        }
        var o = global::ConditioningControlPanel.Avalonia.Program.Win32Options(_ => null, DxgiAdapters.List);
        if (real.TrueForAll(a => a.IsSoftware))   // windows-latest runners: WARP only
            Assert.Equal(new[] { Win32RenderingMode.Software }, o.RenderingMode);
        else
            Assert.Equal(new Win32PlatformOptions().RenderingMode, o.RenderingMode);
    }
}
