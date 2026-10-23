using Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion.Runtime;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Companion.Pages
{
    /// <summary>
    /// PORTED from ConditioningControlPanel/Views/Controls/Companion/Pages/LinksPage.xaml.cs
    /// (5681c132a). "Videos it can play" is the room's own WorkshopLibraryCell, adopted live.
    /// </summary>
    public partial class LinksPage : UserControl
    {
        public LinksPage()
        {
            InitializeComponent();
        }

        /// <summary>WPF LinksPage.OnShown: adopt the live cell, reload its list (RefreshVideoLinkPool).</summary>
        internal void OnShown(CompanionRoomView? room)
        {
            KnowledgeLinks.Load();
            if (room?.FindControl<WorkshopAccordion>("WorkshopZone")?.DataContext is not WorkshopRuntimeVm workshop) return;
            CompanionPageHost.Adopt(workshop.Parts.Library, VideosHost);
            workshop.Parts.Library.RefreshPool();
        }
    }
}
