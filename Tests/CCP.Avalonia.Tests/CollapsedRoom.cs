using Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;

namespace CCP.Avalonia.Tests;

/// <summary>
/// Companion v2 collapses the room (CompanionTabView, WPF CompanionTabView.xaml.cs:36-40). The hero,
/// attention, roster and Workshop drawer have no page of their own, so the zone tests that drive them
/// through the shell show the room in place of the chat page first; the v2 layout itself is
/// CompanionPagesTests.
/// </summary>
internal static class CollapsedRoom
{
    internal static CompanionRoomView Show(MainShellWindow shell)
    {
        var tab = shell.Named<CompanionTabView>("CompanionTab")!;
        var room = tab.FindControl<CompanionRoomView>("Room")!;
        room.IsVisible = true;
        tab.FindControl<Control>("Conversation")!.IsVisible = false;   // it sits on top of the room
        return room;
    }
}
