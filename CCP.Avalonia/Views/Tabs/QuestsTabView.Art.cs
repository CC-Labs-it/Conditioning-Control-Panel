using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Views.Tabs;

public partial class QuestsTabView
{
    // WPF GetQuestArt: successful decodes only, shared by every daily/weekly card, until a mod switch.
    private readonly Dictionary<string, Bitmap> _questArtCache = new(StringComparer.OrdinalIgnoreCase);
    private AppSettings? _questSettings;
    private QuestDefinitionService? _questDefinitions;

    private Bitmap? GetQuestArt(QuestDefinition definition)
    {
        try
        {
            var path = definition.EffectiveImagePath;
            if (string.IsNullOrEmpty(path)) return null;
            if (path.StartsWith(QuestDefinition.PackResources, StringComparison.Ordinal))
            {
                var resource = path[QuestDefinition.PackResources.Length..];
                var replacement = CoreModArt.OverridePath(resource);
                if (replacement != null) path = replacement;
                else
                {
                    resource = QuestArtRules.ModeReplacement(resource, CoreSettings.Current.IsSissyMode, CoreMods.IsCCPDefault) ?? resource;
                    path = "avares://CCP.Avalonia/Resources/" + resource;
                }
            }
            if (_questArtCache.TryGetValue(path, out var cached)) return cached;
            using var stream = path.StartsWith("avares://", StringComparison.Ordinal)
                ? AssetLoader.Open(new Uri(path)) : File.OpenRead(path);
            return _questArtCache[path] = new Bitmap(stream);
        }
        catch { return null; } // WPF: an absent/unusable image leaves the card's existing art alone.
    }

    private void ObserveQuestPresentation()
    {
        _questSettings = CoreSettings.Current;
        _questSettings.PropertyChanged += OnQuestSettingsChanged;
        CoreMods.ModChanged += OnQuestModChanged;
        _questDefinitions = App.QuestDefinitions;
        if (_questDefinitions != null) _questDefinitions.QuestDefinitionsUpdated += OnQuestDefinitionsChanged;
    }

    private void ReleaseQuestPresentation()
    {
        if (_questSettings != null) _questSettings.PropertyChanged -= OnQuestSettingsChanged;
        if (_questDefinitions != null) _questDefinitions.QuestDefinitionsUpdated -= OnQuestDefinitionsChanged;
        CoreMods.ModChanged -= OnQuestModChanged;
        _questSettings = null;
        _questDefinitions = null;
        // The controls may still reference the bitmaps; let them release their own references.
        _questArtCache.Clear();
    }

    private void OnQuestSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(AppSettings.StreakFixCharges) or nameof(AppSettings.IsSissyMode))) return;
        var modeChanged = e.PropertyName == nameof(AppSettings.IsSissyMode);
        Dispatcher.UIThread.Post(() =>
        {
            if (_questSettings == null) return;
            if (modeChanged) _questArtCache.Clear();
            RefreshQuestUI();
        });
    }

    private void OnQuestModChanged(object? sender, ModPackage package) => Dispatcher.UIThread.Post(() =>
    {
        if (_questSettings == null) return;
        _questArtCache.Clear();
        // WPF repaints on a mod switch only while the tab is on screen; showing it repaints anyway.
        if (IsVisible) RefreshQuestUI();
    });

    private void OnQuestDefinitionsChanged() => Dispatcher.UIThread.Post(() =>
    {
        if (_questDefinitions != null && IsVisible) RefreshQuestUI();
    });
}
