# WPF reference pictures

WPF app version: 7.1.14 (Release build of port/7.2 be09ff13f, net10, Windows 11). Windows scaling: 100% (96 dpi).
Auto = offscreen RenderTargetBitmap at 96 dpi (WpfReferenceRender, no services, fresh profile).
Live = PrintWindow capture of the running app on a throwaway CCP_USERDATA_DIR, offline, never signed in.
Counts: 150 rows auto-rendered, 66 live captures, 29 extra auto renders with no row, 39 rows skipped (type threw), 10 rows whose .xaml type was not rendered.

## Rows

row-id | file | auto/live | size | notes
---|---|---|---|---
chaos-hub-window | chaos-hub-window.png | auto | 990x930 | ChaosHubWindow.xaml
chaos-overlay-window | chaos-overlay-window.png | auto | 1680x1050 | ChaosOverlayWindow.xaml
ctrl-attention-check | ctrl-attention-check.png | auto | 84x84 | AttentionCheckControl.xaml
ctrl-emi-dock | ctrl-emi-dock.png | auto | 900x600 | EmiDock.xaml
ctrl-inbox-flyout | ctrl-inbox-flyout.png | auto | 900x600 | InboxFlyout.xaml
ctrl-section-tab-strip | ctrl-section-tab-strip.png | auto | 900x600 | SectionTabStrip.xaml
ctrl-sparkle-wallet | ctrl-sparkle-wallet.png | auto | 138x29 | SparkleWallet.xaml
ctrl-v2-get-it-row | ctrl-v2-get-it-row.png | auto | 900x600 | V2GetItRow.xaml
dialogs-asset-submit | dialogs-asset-submit.png | auto | 620x800 | AssetSubmitDialog.xaml
dialogs-attention-target-editor | dialogs-attention-target-editor.png | auto | 480x520 | AttentionTargetEditorDialog.xaml
dialogs-awareness-app-picker | dialogs-awareness-app-picker.png | auto | 520x560 | AwarenessAppPickerDialog.xaml
dialogs-awareness-consent | dialogs-awareness-consent.png | auto | 580x800 | AwarenessConsentDialog.xaml
dialogs-catalogue-submit | dialogs-catalogue-submit.png | auto | 620x800 | CatalogueSubmitDialog.xaml
dialogs-chaster-import-confirm | dialogs-chaster-import-confirm.png | auto | 440x800 | ChasterImportConfirmDialog.xaml
dialogs-chat-shortcut-capture | dialogs-chat-shortcut-capture.png | auto | 380x800 | ChatShortcutCaptureDialog.xaml
dialogs-color-editor | dialogs-color-editor.png | auto | 450x480 | ColorEditorDialog.xaml
dialogs-companion-phrase-editor | dialogs-companion-phrase-editor.png | auto | 820x620 | CompanionPhraseEditorDialog.xaml
dialogs-companion-prompt-editor | dialogs-companion-prompt-editor.png | auto | 700x750 | CompanionPromptEditorDialog.xaml
dialogs-content-policy-warning | dialogs-content-policy-warning.png | auto | 540x800 | ContentPolicyWarningDialog.xaml
dialogs-display-name | dialogs-display-name.png | auto | 450x240 | DisplayNameDialog.xaml
dialogs-explicit-content-acknowledgement | dialogs-explicit-content-acknowledgement.png | auto | 560x800 | ExplicitContentAcknowledgementDialog.xaml
dialogs-input | dialogs-input.png | auto | 400x180 | InputDialog.xaml
dialogs-knowledge-link-editor | dialogs-knowledge-link-editor.png | auto | 450x800 | KnowledgeLinkEditorDialog.xaml
dialogs-link-phone | dialogs-link-phone.png | auto | 420x560 | LinkPhoneDialog.xaml
dialogs-local-ai-setup-wizard | dialogs-local-ai-setup-wizard.png | auto | 560x650 | LocalAiSetupWizard.xaml
dialogs-lock-card-color | dialogs-lock-card-color.png | auto | 600x700 | LockCardColorDialog.xaml
dialogs-login | dialogs-login.png | auto | 420x560 | LoginDialog.xaml
dialogs-mic-consent | dialogs-mic-consent.png | auto | 640x620 | MicConsentDialog.xaml
dialogs-mod-manager | dialogs-mod-manager.png | auto | 860x800 | ModManagerDialog.xaml
dialogs-mod-picker | dialogs-mod-picker.png | auto | 840x720 | ModPickerDialog.xaml
dialogs-offline-username | dialogs-offline-username.png | auto | 450x300 | OfflineUsernameDialog.xaml
dialogs-profile-customize | dialogs-profile-customize.png | auto | 620x740 | ProfileCustomizeDialog.xaml
dialogs-roadmap-confirm | dialogs-roadmap-confirm.png | auto | 450x280 | RoadmapConfirmDialog.xaml
dialogs-tube-fit | dialogs-tube-fit.png | auto | 760x640 | TubeFitDialog.xaml
dialogs-update-failed | dialogs-update-failed.png | auto | 520x800 | UpdateFailedDialog.xaml
dialogs-update-progress | dialogs-update-progress.png | auto | 400x150 | UpdateProgressDialog.xaml
dialogs-username-picker | dialogs-username-picker.png | auto | 450x350 | UsernamePickerDialog.xaml
dialogs-wardrobe-editor | dialogs-wardrobe-editor.png | auto | 760x560 | WardrobeEditorDialog.xaml
dialogs-warning | dialogs-warning.png | auto | 620x800 | WarningDialog.xaml
dialogs-webcam-consent | dialogs-webcam-consent.png | auto | 640x640 | WebcamConsentDialog.xaml
dialogs-welcome | dialogs-welcome.png | auto | 520x670 | WelcomeDialog.xaml
dialogs-welcome-back-sheet | dialogs-welcome-back-sheet.png | auto | 560x800 | WelcomeBackSheet.xaml
dialogs-whats-new | dialogs-whats-new.png | auto | 520x560 | WhatsNewDialog.xaml
lab-gaze-minigame | lab-gaze-minigame.png | auto | 1100x720 | GazeMinigameWindow.xaml
shell-favorites-rail | shell-favorites-rail.png | auto | 900x600 | SettingsTabView.xaml
shell-file-dialogs | shell-file-dialogs.png | auto | 900x600 | MonitorTargetPicker.xaml
shell-video-playback | shell-video-playback.png | auto | 450x350 | MiniPlayerWindow.xaml
shell-webcam | shell-webcam.png | auto | 1280x800 | WebcamCalibrationWindow.xaml
views-adorned-avatar | views-adorned-avatar.png | auto | 900x600 | AdornedAvatar.xaml
views-companion-ai-page | views-companion-ai-page.png | auto | 900x600 | AiPage.xaml
views-companion-ai-permissions-grid | views-companion-ai-permissions-grid.png | auto | 900x600 | AiPermissionsGrid.xaml
views-companion-attention-gauge | views-companion-attention-gauge.png | auto | 900x600 | AttentionGaugeView.xaml
views-companion-awareness-privacy | views-companion-awareness-privacy.png | auto | 900x600 | AwarenessPrivacyView.xaml
views-companion-chat-threshold | views-companion-chat-threshold.png | auto | 900x600 | ChatThresholdView.xaml
views-companion-engine-room-drawer | views-companion-engine-room-drawer.png | auto | 900x600 | EngineRoomDrawer.xaml
views-companion-hero-card | views-companion-hero-card.png | auto | 900x600 | CompanionHeroCard.xaml
views-companion-keyword-triggers | views-companion-keyword-triggers.png | auto | 900x600 | KeywordTriggersPanel.xaml
views-companion-make-her-yours | views-companion-make-her-yours.png | auto | 900x600 | MakeHerYoursView.xaml
views-companion-memory-diary | views-companion-memory-diary.png | auto | 900x600 | MemoryDiaryView.xaml
views-companion-picker-card | views-companion-picker-card.png | auto | 900x600 | CompanionPickerCard.xaml
views-companion-relationship-constellation | views-companion-relationship-constellation.png | auto | 900x600 | RelationshipConstellation.xaml
views-companion-room | views-companion-room.png | auto | 900x600 | CompanionRoomView.xaml
views-companion-room-preview | views-companion-room-preview.png | auto | 1240x1400 | CompanionRoomPreviewWindow.xaml
views-companion-workshop-accordion | views-companion-workshop-accordion.png | auto | 900x600 | WorkshopAccordion.xaml
views-companion-workshop-awareness-cell | views-companion-workshop-awareness-cell.png | auto | 900x600 | WorkshopAwarenessCell.xaml
views-companion-workshop-behavior-cell | views-companion-workshop-behavior-cell.png | auto | 900x600 | WorkshopBehaviorCell.xaml
views-companion-workshop-community-cell | views-companion-workshop-community-cell.png | auto | 900x600 | WorkshopCommunityCell.xaml
views-companion-workshop-library-cell | views-companion-workshop-library-cell.png | auto | 900x600 | WorkshopLibraryCell.xaml
views-companion-workshop-roster-cell | views-companion-workshop-roster-cell.png | auto | 900x600 | WorkshopRosterCell.xaml
views-companion-workshop-triggers-cell | views-companion-workshop-triggers-cell.png | auto | 900x600 | WorkshopTriggersCell.xaml
views-daily-quest-card | views-daily-quest-card.png | auto | 900x600 | DailyQuestCard.xaml
views-deeper-gaze-picker | views-deeper-gaze-picker.png | auto | 1280x800 | GazePickerWindow.xaml
views-deeper-new-enhancement-dialog | views-deeper-new-enhancement-dialog.png | auto | 540x410 | NewEnhancementDialog.xaml
views-deeper-url-prompt-dialog | views-deeper-url-prompt-dialog.png | auto | 500x190 | UrlPromptDialog.xaml
views-emi-ring-picker | views-emi-ring-picker.png | auto | 900x600 | EmiRingPicker.xaml
views-profile-privacy-panel | views-profile-privacy-panel.png | auto | 900x600 | ProfilePrivacyPanel.xaml
views-settings-account | views-settings-account.png | auto | 900x600 | AccountSettingsSection.xaml
views-settings-audio | views-settings-audio.png | auto | 900x600 | AudioSettingsSection.xaml
views-settings-data | views-settings-data.png | auto | 900x600 | DataSettingsSection.xaml
views-settings-devices | views-settings-devices.png | auto | 900x600 | DevicesSettingsSection.xaml
views-settings-emidesk | views-settings-emidesk.png | auto | 900x600 | EmiDeskSettingsSection.xaml
views-settings-general | views-settings-general.png | auto | 900x600 | GeneralSettingsSection.xaml
views-settings-notifications | views-settings-notifications.png | auto | 900x600 | NotificationsSettingsSection.xaml
views-settings-performance | views-settings-performance.png | auto | 900x600 | PerformanceSettingsSection.xaml
views-settings-updates | views-settings-updates.png | auto | 900x600 | UpdatesSettingsSection.xaml
views-studio-brain-drain-feature | views-studio-brain-drain-feature.png | auto | 900x600 | BrainDrainFeatureControl.xaml
views-studio-ramp-rack | views-studio-ramp-rack.png | auto | 900x600 | RampRackPanel.xaml
views-studio-scheduler-rack | views-studio-scheduler-rack.png | auto | 900x600 | SchedulerRackPanel.xaml
views-tab-achievements | views-tab-achievements.png | auto | 900x600 | AchievementsTabView.xaml
views-tab-app-settings | views-tab-app-settings.png | auto | 900x600 | AppSettingsTabView.xaml
views-tab-assets | views-tab-assets.png | auto | 900x600 | AssetsTabView.xaml
views-tab-available-subjects | views-tab-available-subjects.png | auto | 900x600 | AvailableSubjectsTabView.xaml
views-tab-awareness | views-tab-awareness.png | auto | 900x600 | AwarenessTabView.xaml
views-tab-bambi-takeover | views-tab-bambi-takeover.png | auto | 900x600 | BambiTakeoverTabView.xaml
views-tab-blink-trainer | views-tab-blink-trainer.png | auto | 900x600 | BlinkTrainerTabView.xaml
views-tab-chaster | views-tab-chaster.png | auto | 900x600 | ChasterTabView.xaml
views-tab-companion | views-tab-companion.png | auto | 900x600 | CompanionTabView.xaml
views-tab-deeper | views-tab-deeper.png | auto | 900x600 | DeeperTabView.xaml
views-tab-discord | views-tab-discord.png | auto | 900x600 | DiscordTabView.xaml
views-tab-enhancements | views-tab-enhancements.png | auto | 900x600 | EnhancementsTabView.xaml
views-tab-exclusives | views-tab-exclusives.png | auto | 900x600 | ExclusivesTabView.xaml
views-tab-friends | views-tab-friends.png | auto | 900x600 | FriendsTabView.xaml
views-tab-graded-intake | views-tab-graded-intake.png | auto | 900x600 | GradedIntakeTabView.xaml
views-tab-haptics | views-tab-haptics.png | auto | 900x600 | HapticsTabView.xaml
views-tab-leaderboard | views-tab-leaderboard.png | auto | 900x600 | LeaderboardTabView.xaml
views-tab-leash | views-tab-leash.png | auto | 900x600 | LeashTabView.xaml
views-tab-lockdown | views-tab-lockdown.png | auto | 900x600 | LockdownTabView.xaml
views-tab-play | views-tab-play.png | auto | 900x600 | PlayTabView.xaml
views-tab-presets | views-tab-presets.png | auto | 900x600 | PresetsTabView.xaml
views-tab-programs | views-tab-programs.png | auto | 900x600 | ProgramsTabView.xaml
views-tab-quests | views-tab-quests.png | auto | 900x600 | QuestsTabView.xaml
views-tab-remote-control | views-tab-remote-control.png | auto | 900x600 | RemoteControlTabView.xaml
views-tab-settings | views-tab-settings.png | auto | 900x600 | SettingsTabView.xaml
views-tab-she-listening | views-tab-she-listening.png | auto | 900x600 | SheListeningTabView.xaml
views-tab-spiral | views-tab-spiral.png | auto | 900x600 | SpiralTabView.xaml
views-tab-studio | views-tab-studio.png | auto | 900x600 | StudioTabView.xaml
win-announcement-popup | win-announcement-popup.png | auto | 440x800 | AnnouncementPopup.xaml
win-bubble-count | win-bubble-count.png | auto | 400x300 | BubbleCountWindow.xaml
win-bubble-count-result | win-bubble-count-result.png | auto | 400x300 | BubbleCountResultWindow.xaml
win-bug-report | win-bug-report.png | auto | 760x780 | BugReportWindow.xaml
win-corner-gif | win-corner-gif.png | auto | 460x640 | CornerGifWindow.xaml
win-easter-egg | win-easter-egg.png | auto | 550x650 | EasterEggWindow.xaml
win-emi-codex | win-emi-codex.png | auto | 880x700 | EmiCodexWindow.xaml
win-emi-desk | win-emi-desk.png | auto | 880x463 | EmiDeskWindow.xaml
win-feature-settings | win-feature-settings.png | auto | 320x600 | FeatureSettingsPopup.xaml
win-haptics-setup | win-haptics-setup.png | auto | 520x720 | HapticsSetupWindow.xaml
win-launcher | win-launcher.png | auto | 1280x800 | LauncherWindow.xaml
win-launcher-edge | win-launcher-edge.png | auto | 1280x800 | LauncherWindow.xaml
win-launcher-media-dialog | win-launcher-media-dialog.png | auto | 560x790 | LauncherMediaDialog.xaml
win-layered-audio | win-layered-audio.png | auto | 480x660 | LayeredAudioWindow.xaml
win-lock-card | win-lock-card.png | auto | 1280x800 | LockCardWindow.xaml
win-mantra | win-mantra.png | auto | 1280x800 | MantraWindow.xaml
win-media-history | win-media-history.png | auto | 920x640 | MediaHistoryWindow.xaml
win-mini-player | win-mini-player.png | auto | 450x350 | MiniPlayerWindow.xaml
win-mod-creator | win-mod-creator.png | auto | 1100x750 | ModCreatorWindow.xaml
win-my-reports | win-my-reports.png | auto | 520x480 | MyReportsWindow.xaml
win-phrase-input | win-phrase-input.png | auto | 320x600 | FeatureSettingsPopup.xaml
win-pink-rush | win-pink-rush.png | auto | 350x150 | PinkRushPopup.xaml
win-quest-complete | win-quest-complete.png | auto | 350x130 | QuestCompletePopup.xaml
win-quiz | win-quiz.png | auto | 900x750 | QuizWindow.xaml
win-quiz-category-editor | win-quiz-category-editor.png | auto | 700x750 | QuizCategoryEditorWindow.xaml
win-session-editor | win-session-editor.png | auto | 765x765 | SessionEditorWindow.xaml
win-session-log-history | win-session-log-history.png | auto | 560x640 | SessionLogHistoryWindow.xaml
win-settings-palette | win-settings-palette.png | auto | 620x470 | SettingsPaletteWindow.xaml
win-splash-screen | win-splash-screen.png | auto | 400x215 | SplashScreen.xaml
win-webcam-calibration | win-webcam-calibration.png | auto | 1280x800 | WebcamCalibrationWindow.xaml
win-webcam-gaze-tracker | win-webcam-gaze-tracker.png | auto | 1280x800 | WebcamGazeTrackerWindow.xaml
win-webcam-loading-splash | win-webcam-loading-splash.png | auto | 420x200 | WebcamLoadingSplash.xaml
win-webcam-quick-recal | win-webcam-quick-recal.png | auto | 1280x800 | WebcamQuickRecalWindow.xaml
win-whatmoved-card | win-whatmoved-card.png | auto | 520x800 | WhatMovedCard.xaml

## Live captures

Names: `shell-door-<door>` (rail door), `shell-tab-<pill>` (nav pill), `dialog-from-<pill>` (window that pill opens),
`settings-page-NN` (Settings page scrolled top to bottom), `release-content-mod-manager-rows` (Mod Manager, built-in
CCP Default selected). The launcher has no live capture (PrintWindow returns black for it); use auto/LauncherWindow.png.

name | file | live | size | notes
---|---|---|---|---
dialog-from-medialog | live/dialog-from-medialog.png | live | 920x640 | sandbox profile, offline
dialog-from-mods | live/dialog-from-mods.png | live | 860x697 | sandbox profile, offline
dialog-from-phrases | live/dialog-from-phrases.png | live | 820x620 | sandbox profile, offline
dialogs-ccp-emi-show | live/dialogs-ccp-emi-show.png | live | 1680x1050 | sandbox profile, offline
dialogs-ccp-emi-speech | live/dialogs-ccp-emi-speech.png | live | 390x119 | sandbox profile, offline
dialogs-emi | live/dialogs-emi.png | live | 1680x1050 | sandbox profile, offline
release-content-mod-manager-rows | live/release-content-mod-manager-rows.png | live | 860x697 | sandbox profile, offline
settings-page-00 | live/settings-page-00.png | live | 1560x1002 | sandbox profile, offline
settings-page-01 | live/settings-page-01.png | live | 1560x1002 | sandbox profile, offline
settings-page-02 | live/settings-page-02.png | live | 1560x1002 | sandbox profile, offline
settings-page-03 | live/settings-page-03.png | live | 1560x1002 | sandbox profile, offline
settings-page-04 | live/settings-page-04.png | live | 1560x1002 | sandbox profile, offline
settings-page-05 | live/settings-page-05.png | live | 1560x1002 | sandbox profile, offline
settings-page-06 | live/settings-page-06.png | live | 1560x1002 | sandbox profile, offline
settings-page-07 | live/settings-page-07.png | live | 1560x1002 | sandbox profile, offline
settings-page-08 | live/settings-page-08.png | live | 1560x1002 | sandbox profile, offline
settings-page-09 | live/settings-page-09.png | live | 1560x1002 | sandbox profile, offline
settings-page-10 | live/settings-page-10.png | live | 1560x1002 | sandbox profile, offline
settings-page-11 | live/settings-page-11.png | live | 1560x1002 | sandbox profile, offline
settings-page-12 | live/settings-page-12.png | live | 1560x1002 | sandbox profile, offline
settings-page-13 | live/settings-page-13.png | live | 1560x1002 | sandbox profile, offline
shell-door-companion | live/shell-door-companion.png | live | 1560x1002 | sandbox profile, offline
shell-door-home | live/shell-door-home.png | live | 1560x1002 | sandbox profile, offline
shell-door-library | live/shell-door-library.png | live | 1560x1002 | sandbox profile, offline
shell-door-play | live/shell-door-play.png | live | 1560x1002 | sandbox profile, offline
shell-door-settings | live/shell-door-settings.png | live | 1560x1002 | sandbox profile, offline
shell-door-social | live/shell-door-social.png | live | 1560x1002 | sandbox profile, offline
shell-door-studio | live/shell-door-studio.png | live | 1560x1002 | sandbox profile, offline
shell-door-you | live/shell-door-you.png | live | 1560x1002 | sandbox profile, offline
shell-first-run-wizard-1 | live/shell-first-run-wizard-1.png | live | 900x680 | sandbox profile, offline
shell-first-run-wizard-2 | live/shell-first-run-wizard-2.png | live | 900x680 | sandbox profile, offline
shell-main-window-startup | live/shell-main-window-startup.png | live | 1560x1002 | sandbox profile, offline
shell-tab-achievements | live/shell-tab-achievements.png | live | 1560x1002 | sandbox profile, offline
shell-tab-assets | live/shell-tab-assets.png | live | 1560x1002 | sandbox profile, offline
shell-tab-availablesubjects | live/shell-tab-availablesubjects.png | live | 1560x1002 | sandbox profile, offline
shell-tab-awareness | live/shell-tab-awareness.png | live | 1560x1002 | sandbox profile, offline
shell-tab-bambitakeover | live/shell-tab-bambitakeover.png | live | 1560x1002 | sandbox profile, offline
shell-tab-catalogue | live/shell-tab-catalogue.png | live | 1560x1002 | sandbox profile, offline
shell-tab-chaster | live/shell-tab-chaster.png | live | 1560x1002 | sandbox profile, offline
shell-tab-companion | live/shell-tab-companion.png | live | 1560x1002 | sandbox profile, offline
shell-tab-companionai | live/shell-tab-companionai.png | live | 1560x1002 | sandbox profile, offline
shell-tab-companionlinks | live/shell-tab-companionlinks.png | live | 1560x1002 | sandbox profile, offline
shell-tab-deeper | live/shell-tab-deeper.png | live | 1560x1002 | sandbox profile, offline
shell-tab-discord | live/shell-tab-discord.png | live | 1560x1002 | sandbox profile, offline
shell-tab-enhancements | live/shell-tab-enhancements.png | live | 1560x1002 | sandbox profile, offline
shell-tab-folders | live/shell-tab-folders.png | live | 1560x1002 | sandbox profile, offline
shell-tab-friends | live/shell-tab-friends.png | live | 1560x1002 | sandbox profile, offline
shell-tab-haptics | live/shell-tab-haptics.png | live | 1560x1002 | sandbox profile, offline
shell-tab-justdrop | live/shell-tab-justdrop.png | live | 1560x1002 | sandbox profile, offline
shell-tab-leaderboard | live/shell-tab-leaderboard.png | live | 1560x1002 | sandbox profile, offline
shell-tab-leash | live/shell-tab-leash.png | live | 1560x1002 | sandbox profile, offline
shell-tab-medialog | live/shell-tab-medialog.png | live | 1560x1002 | sandbox profile, offline
shell-tab-mods | live/shell-tab-mods.png | live | 1560x1002 | sandbox profile, offline
shell-tab-permissions | live/shell-tab-permissions.png | live | 1560x1002 | sandbox profile, offline
shell-tab-personality | live/shell-tab-personality.png | live | 1560x1002 | sandbox profile, offline
shell-tab-phrases | live/shell-tab-phrases.png | live | 1560x1002 | sandbox profile, offline
shell-tab-play | live/shell-tab-play.png | live | 1560x1002 | sandbox profile, offline
shell-tab-playeyes | live/shell-tab-playeyes.png | live | 1560x1002 | sandbox profile, offline
shell-tab-playsessions | live/shell-tab-playsessions.png | live | 1560x1002 | sandbox profile, offline
shell-tab-presets | live/shell-tab-presets.png | live | 1560x1002 | sandbox profile, offline
shell-tab-programs | live/shell-tab-programs.png | live | 1560x1002 | sandbox profile, offline
shell-tab-quests | live/shell-tab-quests.png | live | 1560x1002 | sandbox profile, offline
shell-tab-ramp | live/shell-tab-ramp.png | live | 1560x1002 | sandbox profile, offline
shell-tab-remotecontrol | live/shell-tab-remotecontrol.png | live | 1560x1002 | sandbox profile, offline
shell-tab-shelistening | live/shell-tab-shelistening.png | live | 1560x1002 | sandbox profile, offline
shell-tab-studio | live/shell-tab-studio.png | live | 1560x1002 | sandbox profile, offline

## Extra auto renders (type has no ledger row)

name | file | auto | size | type
---|---|---|---|---
(no row) | auto/AppInfoFeatureControl.png | auto | 900x600 | AppInfoFeatureControl
(no row) | auto/AudioPlaylistCard.png | auto | 900x600 | AudioPlaylistCard
(no row) | auto/BouncingTextFeatureControl.png | auto | 900x600 | BouncingTextFeatureControl
(no row) | auto/BubbleCountFeatureControl.png | auto | 900x600 | BubbleCountFeatureControl
(no row) | auto/BubblePopFeatureControl.png | auto | 900x600 | BubblePopFeatureControl
(no row) | auto/ChaosFxWindow.png | auto | 1680x1050 | ChaosFxWindow
(no row) | auto/ChaosIntroWindow.png | auto | 560x800 | ChaosIntroWindow
(no row) | auto/FeatureCard.png | auto | 900x600 | FeatureCard
(no row) | auto/FlashFeatureControl.png | auto | 900x600 | FlashFeatureControl
(no row) | auto/FlashWindow.png | auto | 1280x800 | FlashWindow
(no row) | auto/FriendsRailChip.png | auto | 900x600 | FriendsRailChip
(no row) | auto/GoonTestWindow.png | auto | 1560x940 | GoonTestWindow
(no row) | auto/IntensityRampFeatureControl.png | auto | 900x600 | IntensityRampFeatureControl
(no row) | auto/KnowledgeLinksEditor.png | auto | 900x600 | KnowledgeLinksEditor
(no row) | auto/LinksPage.png | auto | 900x600 | LinksPage
(no row) | auto/LockCardFeatureControl.png | auto | 900x600 | LockCardFeatureControl
(no row) | auto/MercyRow.png | auto | 900x600 | MercyRow
(no row) | auto/MindWipeFeatureControl.png | auto | 900x600 | MindWipeFeatureControl
(no row) | auto/MonitorsSettingsSection.png | auto | 900x600 | MonitorsSettingsSection
(no row) | auto/PermissionsPage.png | auto | 900x600 | PermissionsPage
(no row) | auto/PersonalityPage.png | auto | 900x600 | PersonalityPage
(no row) | auto/PhraseInputDialog.png | auto | 350x150 | PhraseInputDialog
(no row) | auto/PinkFilterFeatureControl.png | auto | 900x600 | PinkFilterFeatureControl
(no row) | auto/SchedulerFeatureControl.png | auto | 900x600 | SchedulerFeatureControl
(no row) | auto/SpiralFeatureControl.png | auto | 900x600 | SpiralFeatureControl
(no row) | auto/SplitFeatureCard.png | auto | 900x600 | SplitFeatureCard
(no row) | auto/SubliminalFeatureControl.png | auto | 900x600 | SubliminalFeatureControl
(no row) | auto/VideoFeatureControl.png | auto | 900x600 | VideoFeatureControl
(no row) | auto/VisualsFeatureControl.png | auto | 900x600 | VisualsFeatureControl

## Skipped rows (the type threw offscreen)

- chaos-hud-window (ChaosHudWindow.xaml): NullReferenceException: Object reference not set to an instance of an object. | at ConditioningControlPanel.ChaosHudWindow..ctor(ChaosRunState state, ChaosModeService chaos) in C:\wt-port72\ConditioningControlPanel\Chaos\ChaosHudWindow.xaml.cs:line 38
- chaos-slot-picker (ChaosSlotPickerWindow.xaml): no public constructor
- ctrl-season-recap-card (SeasonRecapCard.xaml): XamlParseException: È stata generata un'eccezione durante la specifica di un valore su 'System.Windows.StaticResourceExtension'. | at ConditioningControlPanel.Controls.SeasonRecapCard.InitializeComponent() in C:\wt-port72\ConditioningControlPanel\Controls\SeasonRecapCard.xaml:line 1
- ctrl-season-recap-window (SeasonRecapWindow.xaml): XamlParseException: È stata generata un'eccezione durante la specifica di un valore su 'System.Windows.StaticResourceExtension'. | at ConditioningControlPanel.Controls.SeasonRecapWindow.InitializeComponent() in C:\wt-port72\ConditioningControlPanel\Controls\SeasonRecapWindow.xaml:line 1
- dialogs-awareness-preset-detail (AwarenessPresetDetailDialog.xaml): NullReferenceException: Object reference not set to an instance of an object. | at ConditioningControlPanel.AwarenessPresetDetailDialog..ctor(KeywordTriggerPreset preset, Boolean isNewCustomPreset) in C:\wt-port72\ConditioningControlPanel\Dialogs\AwarenessPresetDetailDialog.xaml.cs:line 60
- dialogs-catalogue-picker (CataloguePickerDialog.xaml): NullReferenceException: Object reference not set to an instance of an object. | at ConditioningControlPanel.CataloguePickerDialog..ctor(List`1 entries, String htVideoId) in C:\wt-port72\ConditioningControlPanel\Dialogs\CataloguePickerDialog.xaml.cs:line 43
- dialogs-openai-sampler-settings (OpenAiCompatibleSamplerSettingsDialog.xaml): ArgumentNullException: Value cannot be null. (Parameter 'settings') | at ConditioningControlPanel.OpenAiCompatibleSamplerSettingsDialog..ctor(CompanionPromptSettings settings) in C:\wt-port72\ConditioningControlPanel\Dialogs\OpenAiCompatibleSamplerSettingsDialog.xaml.cs:line 20
- dialogs-profile-privacy (ProfilePrivacyDialog.xaml): NullReferenceException: Object reference not set to an instance of an object. | at ConditioningControlPanel.ProfilePrivacyDialog..ctor(ProfilePrivacyPanel panel) in C:\wt-port72\ConditioningControlPanel\Dialogs\ProfilePrivacyDialog.xaml.cs:line 25
- dialogs-program-enroll (ProgramEnrollDialog.xaml): XamlParseException: È stata generata un'eccezione durante la specifica di un valore su 'System.Windows.StaticResourceExtension'. | at ConditioningControlPanel.ProgramEnrollDialog.InitializeComponent() in C:\wt-port72\ConditioningControlPanel\Dialogs\ProgramEnrollDialog.xaml:line 1
- dialogs-roadmap-diary (RoadmapDiaryDialog.xaml): NullReferenceException: Object reference not set to an instance of an object. | at ConditioningControlPanel.RoadmapDiaryDialog..ctor(String stepId, RoadmapStepDefinition stepDef, RoadmapStepProgress progress) in C:\wt-port72\ConditioningControlPanel\Dialogs\RoadmapDiaryDialog.xaml.cs:line 22
- dialogs-roadmap-start (RoadmapStartDialog.xaml): NullReferenceException: Object reference not set to an instance of an object. | at ConditioningControlPanel.RoadmapStartDialog..ctor(RoadmapStepDefinition stepDef) in C:\wt-port72\ConditioningControlPanel\Dialogs\RoadmapStartDialog.xaml.cs:line 17
- dialogs-roadmap-step-popup (RoadmapStepPopup.xaml): NullReferenceException: Object reference not set to an instance of an object. | at ConditioningControlPanel.RoadmapStepPopup..ctor(RoadmapStepDefinition stepDef, RoadmapStepProgress progress) in C:\wt-port72\ConditioningControlPanel\Dialogs\RoadmapStepPopup.xaml.cs:line 26
- dialogs-text-editor (TextEditorDialog.xaml): ArgumentNullException: Value cannot be null. (Parameter 'dictionary') | at ConditioningControlPanel.TextEditorDialog..ctor(String title, Dictionary`2 data) in C:\wt-port72\ConditioningControlPanel\Dialogs\TextEditorDialog.xaml.cs:line 35
- dialogs-update-notification (UpdateNotificationDialog.xaml): NullReferenceException: Object reference not set to an instance of an object. | at ConditioningControlPanel.UpdateNotificationDialog..ctor(UpdateInfo updateInfo) in C:\wt-port72\ConditioningControlPanel\Dialogs\UpdateNotificationDialog.xaml.cs:line 22
- shell-auto-updater (App.xaml, UpdateNotificationDialog.xaml): NullReferenceException: Object reference not set to an instance of an object. | at ConditioningControlPanel.UpdateNotificationDialog..ctor(UpdateInfo updateInfo) in C:\wt-port72\ConditioningControlPanel\Dialogs\UpdateNotificationDialog.xaml.cs:line 22
- shell-global-hotkeys (MainWindow.xaml): NullReferenceException: Object reference not set to an instance of an object. | at ConditioningControlPanel.MainWindow..ctor() in C:\wt-port72\ConditioningControlPanel\MainWindow\MainWindow.xaml.cs:line 359
- shell-main-window (MainWindow.xaml): NullReferenceException: Object reference not set to an instance of an object. | at ConditioningControlPanel.MainWindow..ctor() in C:\wt-port72\ConditioningControlPanel\MainWindow\MainWindow.xaml.cs:line 359
- shell-tray-icon (MainWindow.xaml): NullReferenceException: Object reference not set to an instance of an object. | at ConditioningControlPanel.MainWindow..ctor() in C:\wt-port72\ConditioningControlPanel\MainWindow\MainWindow.xaml.cs:line 359
- theme-main-window (MainWindow.xaml): NullReferenceException: Object reference not set to an instance of an object. | at ConditioningControlPanel.MainWindow..ctor() in C:\wt-port72\ConditioningControlPanel\MainWindow\MainWindow.xaml.cs:line 359
- theme-season-recap-card (SeasonRecapCard.xaml): XamlParseException: È stata generata un'eccezione durante la specifica di un valore su 'System.Windows.StaticResourceExtension'. | at ConditioningControlPanel.Controls.SeasonRecapCard.InitializeComponent() in C:\wt-port72\ConditioningControlPanel\Controls\SeasonRecapCard.xaml:line 1
- views-companion-v2-conversation (ConversationPage.xaml): no public constructor
- views-deeper-editor-window (DeeperEditorWindow.xaml): NullReferenceException: Object reference not set to an instance of an object. | at ConditioningControlPanel.Views.Deeper.DeeperEditorWindow.LoadEnhancement(Enhancement enhancement, String filePath) in C:\wt-port72\ConditioningControlPanel\Views\Deeper\DeeperEditorWindow.xaml.cs:line 404
- views-deeper-enhancement-player (EnhancementPlayerWindow.xaml): ArgumentNullException: Value cannot be null. (Parameter 'player') | at ConditioningControlPanel.Views.Deeper.EnhancementPlayerWindow..ctor(EnhancementAudioPlayer player, EnhancementHostService host) in C:\wt-port72\ConditioningControlPanel\Views\Deeper\EnhancementPlayerWindow.xaml.cs:line 86
- win-achievement-popup (AchievementPopup.xaml): NullReferenceException: Object reference not set to an instance of an object. | at ConditioningControlPanel.AchievementPopup..ctor(Achievement achievement, String headerIcon, String headerText) in C:\wt-port72\ConditioningControlPanel\Windows\AchievementPopup.xaml.cs:line 26
- win-descent-fuse (DescentFuseWindow.xaml): no public constructor
- win-emi-book (EmiBookWindow.xaml): ArgumentNullException: Value cannot be null. (Parameter 'owner') | at ConditioningControlPanel.EmiBookWindow..ctor(EmiDeskWindow owner) in C:\wt-port72\ConditioningControlPanel\Windows\EmiDesk\EmiBookWindow.xaml.cs:line 118
- win-emi-mute-prompt (EmiMutePromptWindow.xaml): no public constructor
- win-emi-options (EmiOptionsWindow.xaml): ArgumentNullException: Value cannot be null. (Parameter 'owner') | at ConditioningControlPanel.EmiOptionsWindow..ctor(EmiDeskWindow owner) in C:\wt-port72\ConditioningControlPanel\Windows\EmiDesk\EmiOptionsWindow.xaml.cs:line 83
- win-emi-ring (EmiRingWindow.xaml): ArgumentNullException: Value cannot be null. (Parameter 'owner') | at ConditioningControlPanel.EmiRingWindow..ctor(EmiDeskWindow owner) in C:\wt-port72\ConditioningControlPanel\Windows\EmiDesk\EmiRingWindow.xaml.cs:line 259
- win-feature-intro (FeatureIntroPopup.xaml): no public constructor
- win-first-run-wizard (FirstRunWizard.xaml): no public constructor
- win-help-video (HelpVideoWindow.xaml): no public constructor
- win-item-unlocked (ItemUnlockedPopup.xaml): ArgumentNullException: Value cannot be null. (Parameter 'item') | at ConditioningControlPanel.ItemUnlockedPopup..ctor(WardrobeItem item, Int32 stackIndex) in C:\wt-port72\ConditioningControlPanel\Windows\ItemUnlockedPopup.xaml.cs:line 49
- win-pop-quiz (PopQuizWindow.xaml): NullReferenceException: Object reference not set to an instance of an object. | at ConditioningControlPanel.PopQuizWindow..ctor(PopQuizQuestion question, Boolean isTest) in C:\wt-port72\ConditioningControlPanel\Windows\PopQuizWindow.xaml.cs:line 115
- win-programs-intro (ProgramsIntroPopup.xaml): no public constructor
- win-quiz-report (QuizReportWindow.xaml): NullReferenceException: Object reference not set to an instance of an object. | at ConditioningControlPanel.QuizReportWindow..ctor(QuizHistoryEntry entry) in C:\wt-port72\ConditioningControlPanel\Windows\QuizReportWindow.xaml.cs:line 17
- win-session-complete (SessionCompleteWindow.xaml): NullReferenceException: Object reference not set to an instance of an object. | at ConditioningControlPanel.SessionCompleteWindow.ApplyLog(SessionLog log) in C:\wt-port72\ConditioningControlPanel\Windows\SessionCompleteWindow.xaml.cs:line 84
- win-tease-reveal (TeaseRevealPopup.xaml): no public constructor
- win-tutorial-overlay (TutorialOverlay.xaml): NullReferenceException: Object reference not set to an instance of an object. | at ConditioningControlPanel.TutorialOverlay..ctor(Window targetWindow, TutorialService tutorialService) in C:\wt-port72\ConditioningControlPanel\Windows\TutorialOverlay.xaml.cs:line 63

## Rows with no render (the .xaml type is not a Window/UserControl, or was not found)

- shell-app-startup (App.xaml)
- shell-multi-monitor (App.xaml)
- shell-single-instance (App.xaml)
- theme-brushes (Brushes.xaml)
- theme-colors (Colors.xaml)
- theme-controls (Controls.xaml)
- theme-converters (Converters.xaml)
- theme-inputs (Inputs.xaml)
- views-companion-theme (CompanionTheme.xaml)
- views-companion-workshop-cell-theme (WorkshopCellTheme.xaml)
