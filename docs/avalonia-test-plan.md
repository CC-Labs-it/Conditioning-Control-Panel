# Avalonia tester steps

1. WPF `ConditioningControlPanel/MainWindow/MainWindow.Chaster.cs:84-158` Rows: `shell-chaster`
   Link a test Chaster account, enable the tab and price attention/typo. Book time from a bubble:
   the figure appears at the cause over desktop overlays, without focus or input capture.
   Book without a cause: it appears at the cursor on Windows/X11. With no supported native
   window, the visible shell uses the rail flash. The ring pulses in the figure's colour.
2. WPF `ConditioningControlPanel/Controls/ChasterBookedPop.cs` Rows: `shell-chaster`
   Book a burst inside 400 ms: one figure retitles without restarting its life; opposite
   prices netting to zero dismiss it. Separate nearby figures stack; distant figures do not.
   Check source captions, Full/Reduced/Off motion, and automatic disappearance.
3. WPF `ConditioningControlPanel/MainWindow/MainWindow.Chaster.cs:136-158` Rows: `shell-chaster`
   Reach the daily or tab cap. A refused booking shows the localized muted full tag at the
   cause/cursor, at most once per two seconds. A failed placement must not consume the throttle.
4. WPF `ConditioningControlPanel/MainWindow/MainWindow.Chaster.cs:46-53` Rows: `shell-chaster`
   Close the shell while figures are visible: all close. Repeat with panic (Avalonia safety
   requirement): figures and their frame clocks stop immediately. No pop holds app shutdown.
