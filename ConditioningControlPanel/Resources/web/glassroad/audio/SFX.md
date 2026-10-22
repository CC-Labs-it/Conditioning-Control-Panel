# Glassroad sounds

Recorded sound effects play first; WebAudio synthesis on the one context in `audio/bus.js` stays
as the fallback for every cue. No spoken words of any kind: the files are designed effects
generated with no voices, no vocals and no words in the prompt. Pitched synth figures sit on the
engine root (`SOUND.rootHz`, 220 Hz). Mix numbers live in `audio/mix.js` `MIX` (read as
`feel audio.<key>` first, so a later feel group takes over).

## Files

`audio/sfx/*.mp3` (32 files, about 0.55 MB, 44.1 kHz stereo, 112 kbps one-shots, 96 kbps loops),
listed in `audio/sfx-bank.js` (generated data: file, length, loop points). Every file is
normalised to one loudness (loudest 100 ms at -16 dBFS for one-shots, -18 dBFS for loops, peak at
most -1 dBFS); the level per sound is `SFX_GAIN` in `audio/sfxmap.js`.

Masters, prompts, the generator and the build are private, in `C:/Projects/glassroad-audio/sfx/`
(never in this repo): `sounds.mjs` (prompts), `gen.mjs` (ElevenLabs Sound Effects API, idempotent,
call cap), `build.mjs` (analysis, pick, trim, fade, normalise, ship, bank), `audition.html` (every
variant with its prompt and why it shipped). To ship another variant: `picks.json`
`{ "boost": 2 }`, then `node build.mjs`.

## How a cue picks a sound

`sfxPlan(name, payload)` (pure) names the files a cue plays with level, rate and delay. `resolvePlan`
plays the plan from samples only when every file it names decoded; else the synth figure in
`audio/cues.js` / `audio/cues-b1.js` plays the whole cue. `null` = no recorded sound (synth only),
`[]` = silent on purpose. A bot's cue rides the same plan, quieter and panned (`eventLevel`).
Impacts (bump, wall, hop, landings, drift start, throws, hits) get a fixed small rate step per play
so repeats never sound stamped.

| Event | Recorded sound | Notes |
|---|---|---|
| `countdown` / `go` | `countdown`, `go` | soft glass bells, never a buzzer |
| `launch-boost` | `launch` | soft thump into a rising airy whoosh |
| `drift-start` | `drift-start` | glassy swish |
| `drift-tier` | `drift-tick-1..3` | spark ticks, bigger per tier |
| `drift-release` | `drift-release` | tier 0 silent |
| `boost-start` | `boost` | drift full; slip 0.7 rate 1.06; bubble 0.35 rate 1.12; Surge 0.85 rate 0.94; pad and launch play their own |
| `hop` | `hop` | |
| `land` | `land-light` (tier 1 at 0.55, tier 2), `land-heavy` (tier 3) | tier 0 silent |
| `wall` | `wall` | level and rate by dv / `wall.cueFullDvMps` |
| `bump` | `bump` | soft rubbery glass thunk, from `contact.cueMinDvMps`, full at `bumpFullDvMps` |
| `pad` | `pad` | |
| `lap` | `lap`, `final-lap` on the last lap | the finish line plays `finish` only (`audio.setLaps(n)`, default 3) |
| `finish` | `finish` | |
| `item-box` | `item-box` + `roll-tick` x `rollTicks` (player) | ticks slow down over `items.rollS` |
| `item-roll` | `menu-ok` (rate up for self and rare items) | |
| `item-fire` | `fire-throw`, `fire-lob`, `fire-drop`; Surge `boost` | Pink, Glass, Echo, Golden and the incoming warning stay synth |
| `item-hit` | `item-hit` (victim; thrower quieter); `bubble-pop` for far hits | Glass, golden, decoy, mercy verdicts stay synth |
| `bubble-pop` | `bubble-pop` climbing the pentatonic ladder in a streak (capped at an octave) | |
| `overtake` | `overtake` | being passed stays the synth fall |
| `menu-move` / `-ok` / `-back` / `-toggle` | `menu-move`, `menu-ok`, `menu-back` | `menu-deny` synth |
| `edge-*`, `drop`, `rescue-release`, `section`, `effect-*`, `slipstream`, `place-change`, `race-result`, `item-refused` | synth | held tones, beds and chords |

## Engine

Three seamless loops: `engine-idle` (hover hum), `engine-cruise` (airy glide), `engine-air`
(high-speed wind). `speedPct`, the one variable, crossfades them with equal-power curves
(`engineLayers`: idle gone by `engIdleOutPct`, cruise full at `engCruiseFullPct` then dips by
`engCruiseDip`, air from `engAirFromPct`; a boost adds `engBoostAir`). Pitch moves `engSemisLow` to
`engSemisHigh` with speed (3 semitones), drift tier, air and boost add a little, and the whole travel
is capped at `engPitchTravelSemis` (4): no rising whine. A low-pass opens with speed
(`engLpLowHz` to `engLpHighHz`, wider on a boost over `boost.riseS`, back over `boost.tailTcS`).
The loops ride the engine duck and the effect duck like the saws did. The saws (and the section
pad) stay; the saws fall silent once the loops decode and come back only if a loop fails.

Loops are cut with an equal-power crossfade and carry a wrapped pad on each side, so the codec
delay never lands on the seam; `loopStartS` / `loopEndS` in the bank mark the cycle.

Rival engines: the nearest `ai.audibleKarts` bots each get the same cruise and air loops, crossfaded
by their own speedPct, detuned a fixed few cents per racer, panned, quieter with distance
(`rivalNearM` to `rivalFarM`, `rivalSampleGain` at 0 m). Saw plus triangle until the loops decode.

Music (`audio/music.js`): `playChart(url)` loads a chart.json and loops its audio; `playFile(file)`
plays a picked local file at a set bpm (default `clock.bpm`). `beat` reads off the context clock.
