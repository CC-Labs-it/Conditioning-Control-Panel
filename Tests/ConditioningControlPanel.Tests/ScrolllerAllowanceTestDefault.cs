using System.Runtime.CompilerServices;
using ConditioningControlPanel.Services.Fyp.Online;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The online media book and the old machine-wide ledger default to the real user data folder.
/// A test outside the allowance collection that charges (a cache or web gate test) must never
/// write the developer's real book or delete their real old ledger: both start as memory only.
/// The allowance tests point them at a temp folder themselves.
/// </summary>
internal static class ScrolllerAllowanceTestDefault
{
    [ModuleInitializer]
    internal static void KeepTheLedgerInMemory()
    {
        ScrolllerAllowance.LedgerPath = () => null;
        ScrolllerAllowance.LegacyLedgerPath = () => null;
    }
}
