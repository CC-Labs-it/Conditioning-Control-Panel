using Avalonia;
using Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion.Runtime;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Companion.Pages
{
    /// <summary>
    /// PORTED from ConditioningControlPanel/Views/Controls/Companion/Pages/AiPage.xaml.cs (48f587200,
    /// 9db154ab6). Companion > AI adopts the collapsed room's LIVE EngineRoomDrawer ("EngineZone",
    /// expanded), the Workshop Behaviour + Triggers cells and MemoryDiaryView ("MemoryZone"), never
    /// copies, so every handler wired to them keeps meaning the one control on screen
    /// (<see cref="CompanionPageHost.Adopt"/>).
    /// </summary>
    public partial class AiPage : UserControl
    {
        public AiPage()
        {
            InitializeComponent();
        }

        /// <summary>WPF AiPage.OnShown, through ShowTab("companionai") -> OnTabShown.</summary>
        internal void OnShown(CompanionRoomView? room)
        {
            // WPF AiPage.OnShown: a fresh name editor + recap on every show (the account may have changed).
            App.Brain?.EnsureCurrentAccount();
            NameHost.Content = new V2.PreferredNameEditor();
            RecapHost.Content = new V2.ConversationRecap();
            if (room is null) return;
            if (room.FindControl<WorkshopAccordion>("WorkshopZone")?.DataContext is WorkshopRuntimeVm workshop)
            {
                CompanionPageHost.Adopt(workshop.Parts.Behavior, BehaviorHost);
                workshop.Parts.Triggers.SyncSettings(); // WPF MainWindow.CompanionTab.cs:38 reloads the live trigger controls.
                CompanionPageHost.Adopt(workshop.Parts.Triggers, TriggersHost);
            }
            if (room.FindControl<EngineRoomDrawer>("EngineZone") is { } engine)
            {
                // WPF tab.Vm.Sync() re-reads the engine (CompanionRoomRuntimeVm.cs:96) before expanding it.
                if (engine.DataContext is EngineRoomVm vm) { vm.Sync(); vm.IsExpanded = true; }
                CompanionPageHost.Adopt(engine, EngineHost);
            }
            if (room.FindControl<MemoryDiaryView>("MemoryZone") is { } diary)
            {
                diary.ViewModel?.Sync();   // WPF Brain.EnsureCurrentAccount + the memory half of tab.Vm.Sync
                CompanionPageHost.Adopt(diary, MemoryHost);
            }
        }

        /// <summary>WPF AiPage.OnHidden: an armed "forget everything" never survives leaving the page.</summary>
        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == IsVisibleProperty && !IsVisible && MemoryHost.Content is MemoryDiaryView diary)
                diary.ForgetConfirm.Disarm();
        }
    }
}
