# Avalonia leash tester steps

1. Social > Leash with an account held: see its card after offers and the own card; open Assign, poll again, and confirm the same sheet stays open. End the leash and confirm its card disappears. WPF `Controls/Leash/LeashDrawerSection.cs` Rows: `views-tab-leash`
2. Set the held account quiet: Assign, Punish and Tug disable, Reward stays enabled. Open Reward and panic: the sheet closes without sending anything. WPF `Controls/Leash/LeashHolderCard.cs` Rows: `views-tab-leash`
3. Change intensity to Soft: forbidden punishment rows disappear. Reach three punishments today: only the daily-cap explanation remains. WPF `Controls/Leash/LeashHolderCard.cs` Rows: `views-tab-leash`
4. Send an assignment preset, pardon and punishment from their sheets: successful sends close the sheet, failed sends keep it open with the reason; the result clears after three seconds. WPF `Controls/Leash/LeashHolderCard.cs` Rows: `views-tab-leash`
5. Pick a catalogue video or type a Hypnotube number (non-digits are removed); the punishment slider cannot exceed their video maximum. Tug twice within ten seconds: only the first sends. WPF `Controls/Leash/LeashHolderCard.cs` Rows: `views-tab-leash`
6. With receipt support, advance a sent item's receipt to seen while a holder sheet is open: its step wording updates without closing the sheet. Older servers draw no receipt section. WPF `Controls/Leash/LeashHolderCard.cs` Rows: `views-tab-leash`
