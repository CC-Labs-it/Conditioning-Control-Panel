using System.Runtime.CompilerServices;
using ConditioningControlPanel.Services.Fyp.Online;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The online media book and the old machine-wide ledger default to the real user data folder.
/// A test outside the allowance collection that charges (a cache or web gate test) must never
/// write the developer's real book or delete their real old ledger: both start as memory only.
/// The allowance tests point them at a temp folder themselves. The device cache starts with no
/// folder too: a switch-off answer purges it, and a test must never empty the developer's real
/// cache, nor the folders it used before. The cache tests point them at a temp folder themselves.
/// </summary>
internal static class ScrolllerAllowanceTestDefault
{
    [ModuleInitializer]
    internal static void KeepTheLedgerInMemory()
    {
        ScrolllerAllowance.LedgerPath = () => null;
        ScrolllerAllowance.LegacyLedgerPath = () => null;
        ScrolllerDiskCache.RootProvider = () => null;
        ScrolllerDiskCache.RootsFileProvider = () => null;
        ScrolllerDiskCache.SeedRootsProvider = () => System.Array.Empty<string>();
    }
}
