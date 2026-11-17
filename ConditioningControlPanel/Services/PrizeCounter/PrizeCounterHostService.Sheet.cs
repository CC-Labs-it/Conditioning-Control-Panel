using System;
using System.Windows;

namespace ConditioningControlPanel.Services.PrizeCounter;

/// <summary>A window with a Sparkles wallet bar the counter sheet slides down under (the launcher, the panel).</summary>
internal interface ICounterSheetDoor
{
    /// <summary>The bar the wallet sits in: the sheet starts at its bottom edge.</summary>
    FrameworkElement? CounterSheetBar { get; }

    /// <summary>The wallet chip itself.</summary>
    FrameworkElement? CounterSheetChip { get; }
}

/// <summary>
/// The counter SHEET (owner 2026-11-17: "a Tab inside the window that smoothly comes down"): the same
/// relay, identity pin, init and page messages as the counter window, with
/// <see cref="CounterSheetRules.SheetUrl"/> and <c>init.sheet = true</c>, inside the door window. The
/// borderless sheet window is gone; until the in-window view lands, a wallet door opens the counter window.
/// </summary>
internal static partial class PrizeCounterHostService
{
    private static bool _isSheet;
    private static string _startUrl = StartUrl;

    /// <summary>True while the open counter is the sheet (not the 3D window).</summary>
    public static bool IsSheetOpen => _host != null && _isSheet;

    /// <summary>Open the sheet for this launch, or false (the caller opens the window).</summary>
    private static bool TryLaunchSheet(int generation) => false;

    /// <summary>
    /// A wallet door's press while the sheet may be up (<see cref="CounterSheetRules.OnPress"/>). True when
    /// the press was handled here (the second press closed it); false = open.
    /// </summary>
    private static bool SheetPressHandled()
    {
        if (CounterSheetRules.OnPress(IsSheetOpen) != CounterSheetRules.Press.Close) return false;
        App.Logger?.Information("PrizeCounter: wallet pressed again, sheet closing");
        CloseFor(CloseBack);
        return true;
    }
}
