using System.Runtime.CompilerServices;
using ConditioningControlPanel.Controls.Friends;

namespace ConditioningControlPanel.Tests;

/// <summary>The drawer's folds remember themselves in the real user data folder. No test writes
/// the developer's file: the fold state starts in memory for the whole suite.</summary>
internal static class FriendsFoldTestDefault
{
    [ModuleInitializer]
    internal static void KeepTheFoldsInMemory() => FriendsFold.UseMemory();
}
