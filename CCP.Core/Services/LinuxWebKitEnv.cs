using System;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// WebKitGTK's DMA-BUF renderer allocates its frames through GBM; NVIDIA's driver cannot, so every web view
    /// stays blank with "Failed to create GBM buffer" (live-safety S18, RTX 4080, WebKitGTK 2.54.2).
    /// WEBKIT_DISABLE_DMABUF_RENDERER=1 fixes it but also turns off hardware acceleration for every web view
    /// (WebKit AcceleratedBackingStore::canUseHardwareAcceleration returns false with no transport mode), which
    /// would cost the WebGL pages (backroom, spirals) on Mesa GPUs that work today, so it is set only when the NVIDIA
    /// kernel driver is loaded, and never over a value the user set (any value, "0" included, wins).
    /// Applied in the tarball launcher and at the top of Program.Main (Flatpak, AUR and dotnet runs).
    /// </summary>
    public static class LinuxWebKitEnv
    {
        public const string DisableDmabuf = "WEBKIT_DISABLE_DMABUF_RENDERER";

        /// <summary>Present while NVIDIA's kernel module (proprietary or open) is loaded; nouveau creates neither.</summary>
        public static readonly string[] NvidiaProbes = { "/proc/driver/nvidia/version", "/sys/module/nvidia" };

        /// <summary>The value to set for <see cref="DisableDmabuf"/>, or null to leave the environment alone.</summary>
        public static string? DmabufDefault(string? current, Func<string, bool> exists) =>
            current is null && Array.Exists(NvidiaProbes, p => exists(p)) ? "1" : null;
    }
}
