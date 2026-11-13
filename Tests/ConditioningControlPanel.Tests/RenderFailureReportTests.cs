using System.Linq;
using ConditioningControlPanel.Services.Diagnostics;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// ccp-bugs #1565 / #1570 / #1585: a render-thread failure on fullscreen still arrives after the
/// browser hosts moved to a software target; the crash report now names the GPU, the monitors
/// and the visible windows so the next one can point at the surface.
/// </summary>
public sealed class RenderFailureReportTests
{
    [Fact]
    public void Adapter_ReadsAsNameVendorAndMemory()
    {
        Assert.Equal("NVIDIA GeForce RTX 3060 (vendor 0x10DE, 12288 MB dedicated)",
            RenderFailureReport.FormatAdapter(" NVIDIA GeForce RTX 3060 ", 0x10DE, 12288UL * 1024 * 1024));
    }

    [Fact]
    public void Windows_ListLargestFirst_InPhysicalPixels()
    {
        var text = RenderFailureReport.FormatWindows(new[]
        {
            new RenderFailureReport.WindowRow("AvatarTubeWindow", 576, 753, 1.0, "Normal", true, true, false),
            new RenderFailureReport.WindowRow("Window", 1280, 720, 1.5, "Maximized", false, true, true),
        });

        var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        Assert.Equal("- Visible windows: 2", lines[0]);
        Assert.Equal("  - Window 1920x1080 px, dpi 1.50, Maximized, topmost, software", lines[1]);
        Assert.Equal("  - AvatarTubeWindow 576x753 px, dpi 1.00, Normal, transparent, topmost, hardware", lines[2]);
    }

    [Fact]
    public void Windows_AreCapped()
    {
        var rows = Enumerable.Range(0, RenderFailureReport.MaxWindows + 3)
            .Select(i => new RenderFailureReport.WindowRow("W" + i, 100 + i, 100, 1, "Normal", false, false, false))
            .ToArray();
        var text = RenderFailureReport.FormatWindows(rows);
        Assert.Contains("(3 more)", text);
        Assert.Equal(RenderFailureReport.MaxWindows + 2, text.Split('\n').Length);
    }

    [Fact]
    public void Build_NeverThrows_OffTheUiThread()
    {
        var block = RenderFailureReport.Build();
        Assert.StartsWith("Render diagnostics:", block);
        // Off the UI thread: no WPF reads (tier, windows) and no UI stack.
        Assert.DoesNotContain("render tier", block);
        Assert.DoesNotContain("UI stack", block);
    }
}
