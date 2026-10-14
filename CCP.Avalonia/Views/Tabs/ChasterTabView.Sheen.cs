using System;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.Views.Tabs;

public partial class ChasterTabView
{
    private IDisposable? _heroVisibility;
    internal CardSheenAdorner? HeroSheen { get; private set; }

    private void InitializeHeroSheen()
    {
        AttachedToVisualTree += (_, _) =>
        {
            _heroVisibility = EffectiveVisibility.Watch(this, () =>
            {
                RefreshHeroSheen();
                // Parent visibility changes precede the card’s layer/visibility propagation.
                Dispatcher.UIThread.Post(RefreshHeroSheen, DispatcherPriority.Loaded);
            });
            AmbientFxCanvas.Env.MotionGateChanged += RefreshHeroSheen;
            CoreMods.ModChanged += HeroSheenModChanged;
            Dispatcher.UIThread.Post(RefreshHeroSheen, DispatcherPriority.Loaded);
        };
        DetachedFromVisualTree += (_, _) =>
        {
            _heroVisibility?.Dispose(); _heroVisibility = null;
            AmbientFxCanvas.Env.MotionGateChanged -= RefreshHeroSheen;
            CoreMods.ModChanged -= HeroSheenModChanged;
            ParkHeroSheen();
        };
    }

    private void HeroSheenModChanged(object? sender, ModPackage mod) => Dispatcher.UIThread.Post(RefreshHeroSheen);

    private void ParkHeroSheen()
    {
        CardSheenAdorner.Detach(HeroSheen);
        HeroSheen = null;
    }

    private void RefreshHeroSheen()
    {
        // WPF StartHeroSheen / FxPark: a fresh pass each visit, removed when the page hides.
        ParkHeroSheen();
        if (VisualRoot != null && IsEffectivelyVisible && AmbientFxCanvas.Env.AllowAmbientLoops)
            HeroSheen = CardSheenAdorner.Attach(HeroCard, 18);
    }
}
