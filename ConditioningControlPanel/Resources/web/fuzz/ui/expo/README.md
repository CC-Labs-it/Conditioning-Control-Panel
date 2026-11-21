# LLAMA EXPO 2037: first review checkpoint

Steps 1 and 2 of the November 21 brief. The round remains held for owner play.
The in-run build-up, unlock threshold and written story belong to the other lane.

Run `node dev/serve.mjs 8901` from the fuzz folder, then open
`http://localhost:8901/fuzz/dev/play-ending.html?end=ll_end&pictures=none`.
Press **Play Llama Expo 2037**. The ordinary Preview chip also opens `ll_end`.
The local-only **Expo review controls** jump between rooms and add 30 seconds in
the dome. Normal play uses actual active time, independent of effect speed.

## Implemented

- Arrival plaza, hub, six halls, the long secret corridor and a far room.
- WASD/arrows, pointer look with drag fallback, E/click interaction; phone stick,
  drag look and tap interaction. No camera bob, shake, roll or scripted travel.
- Escape uses the game's pause card. Panic disposes the whole Expo immediately.
- The gift shop's temporary exit resolves the existing `ll_end` show. The engine
  still awards its existing sticker and opens the ordinary recap.
- The dome has all six phases at 0/20/45/90/150/210 seconds. Leaving lowers its
  stored time at one quarter speed. Actual visit duration keeps counting at depth 1.
- Loom floor/sky/tunnel, CCP bubble image, pop interaction, fog volumes, moving
  sign words, orbiting framed pictures, glass-like floor and shared breathing.
- Picture panes use the museum's existing consent/cache/player/credit path.
  They remain display-only DOM surfaces; remote pixels never enter WebGL.
- Live Gentle/reduced settings, fewer objects on lite devices, 30 fps lite render
  budget, level horizon and a permanent door through every phase.
- Silent subtitle cues use the supplied PA/dome IDs and exact draft text.

## Placeholders and deliberate deferrals

- Hall interiors and gag states, statues, portraits on plinths, the gift-shop
  loop, the contract wall and the owner's final secret-room line await later steps.
- The gift-shop exit currently works on the first interaction.
- Fog is layered translucent geometry; words are layered planes rather than
  extruded letters; bubbles approximate lit spheres, without final refraction.
- Bubble picture inserts are local llama art or marked placeholders. The framed
  panes use the actual chosen picture source, falling back to marked placeholders.
- Voices and room music are not recorded. The depth-based sound gain contract is
  ready, but the room remains silent apart from the existing recorded bubble pop.
- The plaque reports actual visit duration. Its fictional average is the bible's
  placeholder `0:01`. The final Expo poster/art pass is not part of this checkpoint.

## Lifecycle contracts

`showMaxMs: null` is an explicit player-ended visit, replacing the old 90-second
cap. Other endings retain their caps, whose timers now clear on early completion.
`ending-media` keeps the shared library warm during the visit. Pause and hidden
pages reset input and hold media; panic cancels loading and releases everything.
The constructor also unwinds partially built resources if loading fails.

## Validation

The explicit web test file list passes apart from the known ignored race capture
`assets/race/threshold-loop.mp4`; the existing whole-game headless smoke passes.
Focused tests cover phase boundaries, decay/reentry, room connectivity, collision,
unbounded ending cleanup and live video/GIF motion preferences.

Headless Chromium checks cover walking, touch stick movement, all six phases,
pause/resume, panic, full-depth door exit, gift-shop recap, source revocation,
physical picture alignment, reduced motion and partial-construction rollback.
A real phone frame-rate measurement and owner play remain due.
