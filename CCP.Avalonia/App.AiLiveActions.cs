using System;
using System.Collections.ObjectModel;
using Avalonia.Threading;
using ConditioningControlPanel.Services.Commands;

namespace ConditioningControlPanel.Avalonia;

public partial class App
{
    public static ObservableCollection<string> AiLiveActions { get; } = new();

    internal static void AppendAiLiveAction(string line)
    {
        void Apply() => AiLiveActionFeed.Append(AiLiveActions, line, DateTime.Now);
        if (Dispatcher.UIThread.CheckAccess()) Apply();
        else Dispatcher.UIThread.Post(Apply);
    }
}
