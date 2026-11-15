using System;
using System.Collections.Generic;
using Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Windows;

namespace ConditioningControlPanel.Avalonia.Views.Games;

// WPF ChaosWebViewHost.cs:1776-1807: game hosts suppress companion speech.
internal static class GameActivity
{
    private static readonly HashSet<Window> Open = new();
    internal static event Action? GameActiveChanged;
    internal static bool AnyGameActive => Open.Count != 0;

    static GameActivity()
    {
        foreach (var w in GameWindow.OpenWindowsForTests()) if (Counts(w)) Open.Add(w);
        foreach (var w in IntakeHostWindow.Snapshot()) Open.Add(w);
        Window.WindowOpenedEvent.AddClassHandler<Window>((w, _) =>
        {
            if (Counts(w) && Open.Add(w)) GameActiveChanged?.Invoke();
        });
        Window.WindowClosedEvent.AddClassHandler<Window>((w, _) =>
        {
            if (Open.Remove(w)) GameActiveChanged?.Invoke();
        });
    }

    private static bool Counts(Window w) => w is IntakeHostWindow ||
        w is GameWindow game && game.Spec.Id != GameWindow.FypId && game.Spec.Id != "loom";
}
