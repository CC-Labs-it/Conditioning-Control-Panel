using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Companion.Pages;

// WPF Pages/{Personality,Permissions,Links}Page: borrow live controls, never clone their state.
// ponytail: return them when hidden while the legacy room remains accessible on this head.
public class CompanionOptionsPage : UserControl
{
    private readonly List<Action> _giveBack = new();
    private readonly StackPanel _body = new() { Margin = new Thickness(24,16,24,24), MaxWidth = 1100 };
    private readonly string _kind;
    internal ContentControl PrimaryHost { get; } = new();
    private readonly ContentControl _secondary = new();
    private CompanionPickerCard? _picker;

    protected CompanionOptionsPage(string kind)
    {
        _kind = kind;
        Content = new ScrollViewer { Content = _body, HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        AddText("nav_tab_" + kind, 26);
        AddText("companion_page_" + (kind == "companionlinks" ? "links" : kind) + "_intro", 14);
        if (kind == "personality")
        {
            _picker = new CompanionPickerCard
            {
                MorePersonalityOptions = async owner =>
                {
                    if (owner is MainShellWindow shell) await shell.OpenCompanionPromptEditorAsync();
                }
            };
            _body.Children.Add(_picker);
        }
        if (kind == "companionlinks")
        {
            AddText("companion_page_links_videos", 16);
            AddText("companion_page_links_videos_note", 12);
        }
        _body.Children.Add(PrimaryHost);
        if (kind == "personality")
        {
            AddText("companion_page_personality_advanced", 16);
            AddText("companion_page_personality_advanced_note", 14);
            var advanced = Button("companion_page_personality_open_editor");
            advanced.Name = "BtnOpenPromptEditor";
            advanced.Click += async (_, _) =>
            {
                if (TopLevel.GetTopLevel(this) is MainShellWindow shell) await shell.OpenCompanionPromptEditorAsync();
            };
            _body.Children.Add(advanced);
            AddText("companion_page_personality_community", 16);
            _body.Children.Add(_secondary);
        }
        if (kind == "permissions")
        {
            AddText("companion_page_permissions_takeover", 12);
            var takeover = Button("companion_page_open_takeover");
            takeover.Click += (_, _) => (TopLevel.GetTopLevel(this) as MainShellWindow)?.ShowTab("bambitakeover");
            _body.Children.Add(takeover);
        }
    }

    private void AddText(string key, double size) => _body.Children.Add(new TextBlock
    {
        Text = Loc.Get(key), FontSize = size, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,4,0,12)
    });
    private static Button Button(string key) => new()
    {
        Content = new TextBlock { Text = Loc.Get(key) }, HorizontalAlignment = HorizontalAlignment.Left,
        Margin = new Thickness(0,0,0,16)
    };

    internal void OnShown(CompanionRoomView? room)
    {
        if (room is null || _giveBack.Count != 0) return;
        _picker?.Refresh();
        if (_kind == "personality")
        {
            var presets = room.FindControl<MakeHerYoursView>("PersonalityZone")!;
            presets.ViewModel.Sync();
            AiPage.Borrow(presets, PrimaryHost, _giveBack);
            if (room.WorkshopParts is { } parts) AiPage.Borrow(parts.Community, _secondary, _giveBack);
        }
        else if (_kind == "permissions")
            AiPage.Borrow(room.FindControl<AiPermissionsGrid>("PermissionsZone")!, PrimaryHost, _giveBack);
        else if (room.WorkshopParts is { } parts)
            AiPage.Borrow(parts.Library, PrimaryHost, _giveBack);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != IsVisibleProperty || IsVisible) return;
        for (var i = _giveBack.Count - 1; i >= 0; i--) _giveBack[i]();
        _giveBack.Clear();
    }
}
public sealed class PersonalityPage() : CompanionOptionsPage("personality");
public sealed class PermissionsPage() : CompanionOptionsPage("permissions");
public sealed class LinksPage() : CompanionOptionsPage("companionlinks");
