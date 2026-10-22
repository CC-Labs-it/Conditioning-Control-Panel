namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        internal void RefreshTakeawayShelf() =>
            _ = Named<Tabs.PresetsTabView>("PresetsTab")?.RefreshTakeawayAsync();
    }
}
