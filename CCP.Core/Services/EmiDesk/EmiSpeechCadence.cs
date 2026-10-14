using System;

namespace ConditioningControlPanel.Services.EmiDesk;

/// <summary>The locked desk speech timing, shared by the WPF chain and Avalonia's bubble timer.</summary>
public static class EmiSpeechCadence
{
    public const int Dot1Ms = 420;
    public const int Dot2Ms = 420;
    public const int Dot3Ms = 520;
    public const int RestMs = 200;
    public const int SummonGreetingMs = 2600;

    public static int HoldMs(string? line) => Math.Max(4050, 1890 + 61 * (line?.Length ?? 0));

    public static string Greeting(bool firstContactSpent, bool desktopFirstBootSpent)
        => !firstContactSpent ? "firstContact" : !desktopFirstBootSpent ? "desktopFirstBoot" : "summoned";
}
