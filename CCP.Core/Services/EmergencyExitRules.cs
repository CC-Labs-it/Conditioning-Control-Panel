using System;
using System.Linq;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Services;

// WPF EmergencyExitHostService.cs:204-209, 420-476. The host owns the roll.
public static class EmergencyExitRules
{
    public static string Pick(string last, Random random)
    {
        var pool = new[] { "labyrinth", "password", "jigsaw", "captcha" }.Where(g => g != last).ToArray();
        return pool[random.Next(pool.Length)];
    }

    // WPF EmergencyExitHostService.BuildMod: builtin addresses override manifest values.
    public static (string Honorific, string Subject) Address(string id, string? name, string? petName) => id switch
    {
        "builtin-bambisleep" => ("good girl", "Bambi"),
        "builtin-sissyhypno" => ("sissy", "sissy"),
        "builtin-locked" => ("pet", "pet"),
        "" or "builtin-ccp-default" => (VocabTokens.VanillaPetName, VocabTokens.VanillaPetName),
        _ => (string.IsNullOrWhiteSpace(petName) ? VocabTokens.VanillaPetName : petName.Trim(),
              string.IsNullOrWhiteSpace(name) ? VocabTokens.VanillaPetName : name)
    };

    public static double SendBackChance(string game) => game == "labyrinth" ? 1.0 : 0.33;
    public static bool SendBack(string game, string? result, double roll) =>
        !string.Equals(result?.Trim(), "completed", StringComparison.OrdinalIgnoreCase)
        || roll < SendBackChance(game);
}
