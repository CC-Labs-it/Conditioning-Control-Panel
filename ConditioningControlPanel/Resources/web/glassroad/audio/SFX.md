# Glassroad sounds (B1)

Every sound is WebAudio synthesis on the one context in `audio/bus.js`. No recorded files ship in
B1 and no spoken words of any kind. Pitched figures sit on the engine root (`SOUND.rootHz`, 220 Hz).
Mix numbers live in `audio/mix.js` `MIX` (read as `feel audio.<key>` first, so a later feel group
takes over).

| Event | Who hears it | Sound |
|---|---|---|
| `item-box` | player; near bots quieter | card flip swish; the player also gets roll ticks over `items.rollS`, slowing down |
| `item-roll` | player | draw chime: two notes, a self item a third, a rare one three notes |
| `item-fire` | firer; near bots; full when the target is you | throw whoosh, lob whistle, Pink sweep chord, trap drop plop, Surge whoosh, Glass ring, Echo three taps, Golden shimmer |
| `item-refused` | player | two dull low blips |
| `item-hit` | victim, thrower, near bots | DtRH pop plus a figure per kind for the victim; Glass soak ring, gold ping, decoy hollow pop, mercy soft pop |
| `bubble-pop` | player; near bots | DtRH pop climbing a pentatonic ladder while pops come inside `popStreakS` |
| `effect-start` / `effect-end` | player | a quiet bed per kind under the effect (never louder than the engine idle) and a duck of engine, rivals and music |
| `bump` | both karts; near bots | thump scaled by dv from `contact.cueMinDvMps` to `bumpFullDvMps` |
| `slipstream` | player | held air while charging, cut on fire (the `boost-start` slip cue plays the lift) |
| `boost-start` | player | slip air lift, bubble sparkle, Surge tail, pad whoosh (Phase A cue, kinds added) |
| `overtake` | player | two rising notes; a soft fall when passed |
| `place-change` | player | a lift only on taking the lead |
| `race-result` | player | a figure by place (lead, podium, the rest) |
| `racer-event` | near bots | hop, land, wall, drift start, boost, pad echoed quieter and panned; held tones never |
| `menu-*` (UI) | `audio.cue(name)` | move, ok, back, toggle, deny blips |

Rival engines: the nearest `ai.audibleKarts` bots (from `racers.nearby`) each get a saw plus
triangle voice, panned, quieter with distance (`rivalNearM` to `rivalFarM`) and detuned per racer.

Music (`audio/music.js`): `playChart(url)` loads a chart.json and loops its audio; `playFile(file)`
plays a picked local file at a set bpm (default `clock.bpm`). `beat` reads off the context clock.
No chart ships in B1 (the masters are private).
