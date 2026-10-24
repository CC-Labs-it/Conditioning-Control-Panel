using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Data.Converters;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.UI;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    private readonly List<IDisposable> _navLabelBindings = new();
    private bool _navLabelsClosed;

    // WPF ApplyModToNavRailLabels: keep every caption bound to the localization indexer.
    private void ApplyModToNavRailLabels()
    {
        ClearNavLabelBindings();
        foreach (var row in _navSectionRows)
            if (row.Label is not null && NavSections.Find(row.Section) is { } section)
                BindNavLabel(row.Label, section.LabelKey, caps: true);
        BindNavLabel(Named<TextBlock>("TxtNavBackLabel"), "nav_back_label");
        BindNavLabel(Named<TextBlock>("TxtNavSearchLabel"), "nav_search_label");
        // Deliberate divergence: WPF has no Web App rail caption; its PlayTabView.xaml:590 card title
        // is a plain {loc:Str}. This head's rail caption follows the mod like its Back/Search siblings.
        BindNavLabel(Named<TextBlock>("TxtNavWebAppLabel"), "nav_door_webapp");
    }

    private void BindNavLabel(TextBlock? label, string key, bool caps = false)
    {
        if (label is null) return;
        _navLabelBindings.Add(label.Bind(TextBlock.TextProperty, new Binding($"[{key}]")
        {
            Source = LocalizationManager.Instance, Mode = BindingMode.OneWay,
            Converter = new NavModLabelConverter(key, caps),
        }));
    }

    private void ClearNavLabelBindings()
    {
        foreach (var binding in _navLabelBindings) binding.Dispose();
        _navLabelBindings.Clear();
    }

    private sealed class NavModLabelConverter(string key, bool caps) : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var localized = value as string ?? string.Empty;
            string text;
            try { text = ModAwareLabelRules.Resolve(localized, LocalizationManager.Instance.GetEnglish(key), CoreMods.MakeModAware); }
            catch { text = localized; }
            return caps ? text.ToUpper(CultureInfo.CurrentUICulture) : text;
        }
        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => BindingOperations.DoNothing;
    }
}
