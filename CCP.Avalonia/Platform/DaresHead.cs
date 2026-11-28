using ConditioningControlPanel.Services.Dares;

namespace ConditioningControlPanel.Avalonia.Platform;

// The live Dares host is a separate port; until it starts the drawer stays dark.
internal static class DaresHead
{
    internal static IDaresService? Service { get; set; }
}
