using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.VisualTree;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Companion.Pages
{
    /// <summary>
    /// PORTED from ConditioningControlPanel/Views/Controls/Companion/Pages/CompanionPageHost.cs
    /// (5681c132a). The Companion pages (Personality, Permissions, Links, AI) host the LIVE controls
    /// the v2 ConversationPage collapsed away with the room: the room's zones and Workshop cells,
    /// never copies, so every handler wired to them keeps meaning the one control on screen.
    /// </summary>
    internal static class CompanionPageHost
    {
        /// <summary>Takes <paramref name="element"/> off whatever holds it and puts it in <paramref name="host"/>.</summary>
        public static void Adopt(Control element, ContentControl host)
        {
            if (ReferenceEquals(host.Content, element)) return;
            Detach(element);
            host.Content = element;
        }

        public static void Detach(Control element)
        {
            if (element.Parent is Panel panel) panel.Children.Remove(element);
            else if (element.Parent is ContentControl control && ReferenceEquals(control.Content, element)) control.Content = null;
            else if (element.Parent is Decorator decorator) decorator.Child = null;
            // A Workshop cell sits in the accordion's templated presenter (its logical parent is the
            // presenter). SetCurrentValue keeps the template's {Binding Content}.
            else if (element.GetVisualParent() is ContentPresenter presenter)
                presenter.SetCurrentValue(ContentPresenter.ContentProperty, null);
        }
    }
}
