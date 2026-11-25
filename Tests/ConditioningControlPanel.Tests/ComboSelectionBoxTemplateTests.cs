using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ConditioningControlPanel.Services.Deeper;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// ccp-bugs #1741: the Velvet Kit ComboBox (the implicit style in Resources/Theme/Inputs.xaml)
/// showed the item's type name in the CLOSED box when the combo used DisplayMemberPath, because
/// its ContentSite did not bind ContentTemplateSelector (WPF carries DisplayMemberPath into the
/// selection box through ItemTemplateSelector). These pin the binding and the belt on the model.
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class ComboSelectionBoxTemplateTests
{
    /// <summary>No ToString override on purpose: the closed box must read Name through the template.</summary>
    private sealed class PlainItem
    {
        public string Name { get; set; } = "";
    }

    private static Style VelvetComboStyle()
    {
        var dictionary = new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/ConditioningControlPanel;component/Resources/Theme/Inputs.xaml",
                             UriKind.Absolute)
        };
        return Assert.IsType<Style>(dictionary[typeof(ComboBox)]);
    }

    private static T? FindChild<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T hit) return hit;
            var deeper = FindChild<T>(child);
            if (deeper != null) return deeper;
        }
        return null;
    }

    [Fact]
    public void TheClosedBoxShowsTheDisplayMemberNotTheTypeName() => WpfRenderHarness.OnStaThread(() =>
    {
        var combo = new ComboBox
        {
            Style = VelvetComboStyle(),
            Width = 240,
            Height = 34,
            DisplayMemberPath = nameof(PlainItem.Name),
            ItemsSource = new[] { new PlainItem { Name = "First file" }, new PlainItem { Name = "Second file" } }
        };
        combo.SelectedIndex = 1;

        var host = new Border { Background = Brushes.Black, Child = combo };
        host.Measure(new Size(240, 34));
        host.Arrange(new Rect(0, 0, 240, 34));
        host.UpdateLayout();

        var site = Assert.IsType<ContentPresenter>(combo.Template.FindName("ContentSite", combo));
        Assert.NotNull(site.ContentTemplateSelector);
        Assert.Same(combo.ItemTemplateSelector, site.ContentTemplateSelector);

        var text = FindChild<TextBlock>(site);
        Assert.NotNull(text);
        Assert.Equal("Second file", text!.Text);
    });

    [Fact]
    public void AnItemTemplateStillWinsOverTheSelector() => WpfRenderHarness.OnStaThread(() =>
    {
        // Combos that carry their own ItemTemplate must look exactly as before: ContentTemplate
        // beats ContentTemplateSelector on a ContentPresenter.
        var template = new DataTemplate();
        var factory = new FrameworkElementFactory(typeof(TextBlock));
        factory.SetValue(TextBlock.TextProperty, "from the item template");
        template.VisualTree = factory;

        var combo = new ComboBox
        {
            Style = VelvetComboStyle(),
            Width = 240,
            Height = 34,
            ItemTemplate = template,
            ItemsSource = new[] { new PlainItem { Name = "Only file" } }
        };
        combo.SelectedIndex = 0;

        var host = new Border { Background = Brushes.Black, Child = combo };
        host.Measure(new Size(240, 34));
        host.Arrange(new Rect(0, 0, 240, 34));
        host.UpdateLayout();

        var site = Assert.IsType<ContentPresenter>(combo.Template.FindName("ContentSite", combo));
        var text = FindChild<TextBlock>(site);
        Assert.NotNull(text);
        Assert.Equal("from the item template", text!.Text);
    });

    [Fact]
    public void ADeeperLibraryEntryReadsAsItsFileName()
    {
        var entry = new EnhancementLibraryEntry { Name = "Night walk", FilePath = @"C:\x\night.ccpenh.json" };
        Assert.Equal("Night walk", entry.ToString());
    }
}
