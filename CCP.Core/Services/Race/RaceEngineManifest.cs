using System;
using System.Runtime.InteropServices;

namespace ConditioningControlPanel.Services.Race;

/// <summary>B2 section 1: release pins, never resolved from latest at runtime.</summary>
public sealed record RaceEnginePackage(string Version, string Architecture, Uri Url, string Sha256)
{
    public string DirectoryName => $"electron-{Version}";
}

public static class RaceEngineManifest
{
    // Pins from https://github.com/electron/electron/releases/download/v45.1.0/SHASUMS256.txt
    public const string Version = "45.1.0";
    public static RaceEnginePackage For(Architecture architecture) => architecture switch
    {
        Architecture.X64 => Package("linux-x64", "9dafab2c57ce209f8f716f8f3e4fddd8ffe0820e2455bfbbfc00b0fc13e03a3b"),
        Architecture.Arm64 => Package("linux-arm64", "64b78d93d1129ab805cdc0f2710f446716afbe814a1d08e639dab3175d2285a1"),
        _ => throw new PlatformNotSupportedException("Race engine CPU is not supported.")
    };

    private static RaceEnginePackage Package(string arch, string hash) => new(Version, arch,
        new Uri($"https://github.com/electron/electron/releases/download/v{Version}/electron-v{Version}-{arch}.zip"), hash);
}
