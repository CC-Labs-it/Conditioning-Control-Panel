using System.Collections.Generic;
using System.Windows.Input;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Companion;

/// <summary>WPF MockWorkshopAccordionVm.Collapsed: the six synthetic pigeonholes used by every
/// page-state exhibit. No live controls, settings, dialogs or network actions.</summary>
internal sealed class WorkshopPreviewVm : CompanionObservable, IWorkshopAccordionVm
{
    // ponytail: reuse the head's relay command; WPF's mock invocation tags have no consumer here.
    private static readonly ICommand NoOp = new CompanionRelayCommand(() => { });
    private bool _isExpanded;
    public bool IsExpanded { get => _isExpanded; set => Set(ref _isExpanded, value); }
    public string DrawerNote => Loc.Get("companion_workshop_drawer_note");
    public ICommand FocusCellCommand => NoOp;
    private static CompanionWorkshopRow Row(string label, string? value = null) =>
        new(label, value) { ActivateCommand = NoOp };

    public IReadOnlyList<IWorkshopCellVm> Cells { get; } = new IWorkshopCellVm[]
    {
        new CompanionWorkshopCell(CompanionRoomAnchors.WorkshopRosterCell,
            Row("Synthetic Blowdoll", "Lv 12"),
            Row("Perfect Fuckpuppet", "[100]"),
            Row("Brainwashed Slavedoll", "[125]"),
            Row("Platinum Puppet", "[150]"),
            Row("Bambi Cow", "[75]"),
            Row("assign personality")),
        new CompanionWorkshopCell(CompanionRoomAnchors.WorkshopBehaviorCell,
            CompanionWorkshopRow.Slider("Idle interval", "120s", 0.38),
            CompanionWorkshopRow.Slider("Bubble time", "2s", 0.20),
            Row("Chat shortcut", "Ctrl+T"),
            Row("Camera shortcut", "Ctrl+Alt+K"),
            Row("Mute whispers", "on"),
            Row("Pause in browser", "off"),
            CompanionWorkshopRow.Caption("idle interval is set by her Proactivity trait — override here")),
        new CompanionWorkshopCell(CompanionRoomAnchors.WorkshopTriggersCell,
            Row("Trigger mode · interval", "60s"),
            Row("Edit trigger phrases"),
            Row("Manage phrases", "128 active"),
            Row("Phrase presets · save / delete")),
        new CompanionWorkshopCell(CompanionRoomAnchors.WorkshopLibraryCell,
            Row("Hypnotube link pool", "Bambi Sleep scope"),
            Row("+ add link", "14 links")),
        new CompanionWorkshopCell(CompanionRoomAnchors.WorkshopCommunityCell,
            Row("Browse shared companions"),
            Row("Import / Export"),
            Row("Refresh installed", "2")),
        new CompanionWorkshopCell(CompanionRoomAnchors.WorkshopAwarenessCell,
            CompanionWorkshopRow.Slider("Cooldown", "90s", 0.45),
            CompanionWorkshopRow.Slider("Max cooldown", "off", 0.0),
            Row("Privacy notice · full text"))
    };
}
