using System;
using System.Linq;

namespace ConditioningControlPanel.Services;

// WPF EmergencyExitHostService.cs:204-209, 420-476. The host owns the roll.
public static class EmergencyExitRules
{
    public static string Pick(string last, Random random)
    {
        var pool = new[] { "labyrinth", "password", "jigsaw", "captcha" }.Where(g => g != last).ToArray();
        return pool[random.Next(pool.Length)];
    }

    public static double SendBackChance(string game) => game == "labyrinth" ? 1.0 : 0.33;
    public static bool SendBack(string game, string? result, double roll) =>
        !string.Equals(result?.Trim(), "completed", StringComparison.OrdinalIgnoreCase)
        || roll < SendBackChance(game);
}
