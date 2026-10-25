
# Section tab strip tester steps

1. WPF `ConditioningControlPanel/Controls/NavRail/SectionTabStrip.Help.cs`: open Studio > Presets. Just Drop follows Haptics and precedes Ramp. Clicking asks Open it / Not now; Not now and Escape keep Presets selected. Open it uses the existing server-gated shop host; an unavailable door is a no-op. With a live door, repeat and confirm the shop is refocused. Rows: `ctrl-section-tab-strip`
2. WPF `ConditioningControlPanel/Controls/NavRail/SectionTabStrip.xaml.cs`: Right from Haptics focuses Just Drop; Enter/Space asks. Rebuild language, change section or panic while the card is open: it closes. Repeat at Full, Reduced and Off. Rows: `ctrl-section-tab-strip`
3. WPF `ConditioningControlPanel/MainWindow/MainWindow.SectionChrome.cs`: change language on Settings > General and check the crumb rereads its label. The old together key says Moved for three hits, holds 3.2 seconds and completes the 300 ms fade; Off hides at once. Rows: `ctrl-section-tab-strip`
