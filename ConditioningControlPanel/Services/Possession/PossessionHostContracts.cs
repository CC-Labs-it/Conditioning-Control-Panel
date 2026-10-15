using System.Windows;
using System.Windows.Controls;

namespace ConditioningControlPanel.Services.Possession;

// The WPF half of the Possession contracts. The head-neutral contracts (target, host, attribution,
// context, effect, warden) live in CCP.Core/Services/Possession/PossessionContracts.cs with the control
// as a plain object; this file gives the WPF effects back the typed members they were written against.

/// <summary>What a WPF window adds to <see cref="IPossessionHost"/>.</summary>
public interface IWpfPossessionHost : IPossessionHost
{
    Window Window { get; }
    /// <summary>Transparent, non-hit-testable Canvas stretched over the whole window (last child of
    /// RootGrid, above everything). Ghosts, ember overlays and the cursor ring live here.</summary>
    Canvas GhostLayer { get; }
    /// <summary>Canvas strip along the bottom of the window where fallen pieces come to rest.</summary>
    Canvas RubbleFloor { get; }
    /// <summary>Top-left of the element in GhostLayer coordinates.</summary>
    Point PointOf(FrameworkElement element);
}

/// <summary>The typed reads the effects, scenes and attribution use. Every WPF host is an
/// IWpfPossessionHost and every WPF target holds a FrameworkElement, so the casts cannot miss.</summary>
public static class PossessionWpfContracts
{
    extension(PossessionTarget target)
    {
        public FrameworkElement Element => (FrameworkElement)target.Visual;
    }

    extension(IPossessionHost host)
    {
        public Window Window => ((IWpfPossessionHost)host).Window;
        public Canvas GhostLayer => ((IWpfPossessionHost)host).GhostLayer;
        public Canvas RubbleFloor => ((IWpfPossessionHost)host).RubbleFloor;
        public Point PointOf(FrameworkElement element) => ((IWpfPossessionHost)host).PointOf(element);
    }
}
