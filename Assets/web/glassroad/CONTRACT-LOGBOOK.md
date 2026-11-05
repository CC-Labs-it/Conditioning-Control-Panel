# Glassroad Logbook contract (2026-10-26)

The seam between the Logbook lanes: recorder, recap, record page, Time Trial and the account record.
Owner picks (2026-10-26, all recommendations): cosmetic rewards only; own logbook with race XP into the
CCP level under a daily cap (later wave); unlocks show on the recap only, never mid-race; Time Trial
first; public boards take only signed online results (and later checked Time Trials); about 30
achievements, 3 to 5 hidden; a three-page skippable recap.

Frozen shapes below. A lane that needs a new field adds it as OPTIONAL and says so in its PR; it never
renames or removes one. Everything under `meta/` is pure (no DOM, no three.js, no sim imports beyond
`sim/state.js` constants) and has node tests.

## 1. Files and owners

| File | Owner lane | What |
|---|---|---|
| `meta/record.js` | base (frozen) | Record v1: `emptyRecord`, `cleanRecord`, `mergeRecords`, `loadRecord`, `saveRecord` |
| `meta/medals.js` | TRIAL fills the numbers | `MEDALS`, `medalFor(trackId, timeS)`, `medalRank` |
| `meta/runLog.js` | META | the recorder on the bus -> RunSummary |
| `meta/applyRun.js` | META | `applyRun(record, summary) -> { record, diff }` |
| `meta/achievements.js` | META | the achievement table + `evaluate` |
| `meta/unlocks.js` | META | `UNLOCKS` (id -> label, garage key), `unlockedSet(record)` |
| `meta/recap.js` | META | `buildRecap(summary, diff, before)` -> the recap payload (section 5) |
| `meta/ghost.js` | TRIAL | ghost record / encode / decode / sample |
| `meta/profileSync.js` | SYNC | account record GET/PUT through the lobby transport |
| `hud/recap.js`, results branch of `hud/cards.js` | RECAP | the three recap pages |
| `hud/record.js`, `hud/garage.js`, `render/garage.js`, `hud/garageState.js`, `hud/garage.css` | RECORD | Record tab, achievement wall, garage locks from unlocks |
| `hud/trackPicker.js`, mode row | TRIAL | Race / Time Trial choice |
| `boot.js` | META owns the finish/result region; TRIAL owns `buildRun` / `startRace` options; SYNC adds one call after the record save. Keep each edit small and local. |

## 2. Events the recorder reads

Untagged events on the main bus are the PLAYER's (bots arrive wrapped as `racer-event { racer, name, p }`).
Events tagged with `racer` / `by` / `victim` / `a,b` name kart indexes; the player is index 0.
META adds two events (sim side, additive, no behaviour change): `song-gate { id, passed: bool }` and
`phrase-gate { id, passed: bool }` from `sim/songGates.js` / `sim/phraseGates.js` (player only).

## 3. RunSummary v1 (`meta/runLog.js` `finish()` returns it)

```js
{
  v: 1,
  track: 'threshold',            // course id
  mode: 'race' | 'trial',
  online: false,
  laps: 3, seed: 123,
  assist: 'light' | 'strong' | 'off',
  gentle: false,
  finished: true,                // false = quit or DNF (the results card still shows)
  place: 2, of: 8,               // trial: 1 of 1
  timeS: 182.41, bestLapS: 58.9,
  lapTimesS: [61.2, 58.9, 62.3],
  sectorsS: [[20.1, 19.8, 21.3], ...],   // per lap, thirds of the course by track s
  placeAtLap: [8, 4, 2],         // place when each lap ended
  ledEveryLap: false,            // 1st at the end of every lap
  leadS: 41.2,                   // seconds in 1st
  topSpeedPct: 1.12, distanceM: 4310,
  driftStarts: 40, driftBoosts: [12, 9, 7],   // counts by tier 1, 2, 3
  slipstreams: 6, pads: 14,
  airS: 11.4, longestAirS: 2.1, jumps: 22,
  tricks: 5, bestTrick: 'clean' | null,
  overtakes: 9,
  walls: 3, falls: 1, rescues: 1, fallsAtS: [1820.5],   // fallsAtS capped at 20
  hitsTaken: 2, itemsUsed: 7, itemsLanded: 3,
  tetherBreaks: 1, discBankHits: 0, bigSpiralMax: 2,    // most karts one Big Spiral caught
  traps: { keepGate: 0, keepChain: 1, moving: 0, goonworks: 0, scan: 0, perfume: 0, industry: 0 },
  mirrorsBroken: 0, mirrorStations: 0,
  wordsCaught: 14, wordsOffered: 15,
  songGatesPassed: 6, songGatesOffered: 8,
  cleanLaps: 1,                  // laps with no wall, fall or hit taken
  at: '2026-10-26T12:00:00.000Z',
}
```
Unknown or not-applicable counters are 0, never missing.

## 4. Record v1 (`meta/record.js`, frozen)

localStorage key `glassroad.record.v1` (every access in try/catch). Shape in `emptyRecord()`.
`mergeRecords(a, b)` is the ONE merge rule (client and server use the same arithmetic):
counters take the max, best times take the smallest positive, medals take the higher rank,
achievements keep the earliest time per id, unlocks are a union, recent is the union by `at`
(newest 20). Merging never lowers anything. `cleanRecord` clamps numbers and drops unknown keys,
so a hand-edited or old record can never break the page.

`applyRun(record, summary)` (META) returns `{ record, diff }` and never mutates its input:
```js
diff = {
  pbRace: false, pbLap: true, pbTrial: false,     // a new best on this course for this mode
  medal: null | { from: null | 'bronze' | ..., to: 'gold' },   // trial only
  newAchievements: ['long-hold'],                 // ids, in table order
  newUnlocks: ['kart:velvet-lock'],
}
```
Achievements are evaluated with the record AFTER the run's totals land, so lifetime rules work.

## 5. Recap payload (META builds, RECAP renders)

`hud.setCard('results', { ...result, recap })` where `recap` is `null` (no recorder, e.g. a boot check)
or:
```js
{
  mode: 'race' | 'trial',
  highlights: [{ key: 'driftBoosts3', label: 'Drift boosts, tier 3', value: '7' }],   // exactly 3, strings
  sketch: { fallsAtS: [..], bestSector: 0 | 1 | 2 | null, lengthM: 4310 },
  pb: { race: false, lap: true, trial: false },
  medal: null | { from, to, times: { bronze, silver, gold, glass } },
  achievements: [{ id, name, line }],     // new this run
  unlocks: [{ id, label }],               // new this run
  xp: null,                               // CCP XP wave fills it later: { gain, capLeft }
}
```
Page 1 is today's podium card. Page 2 = highlights + sketch. Page 3 shows only when pb / medal /
achievements / unlocks has anything; otherwise page 2 ends the recap. Throttle / Enter / pad A step
forward; Again and Menu stay on every page. Gentle visuals: fades only, no stamp thunk.

## 6. Unlock ids

`kart:velvet-lock`, `kart:undertow`, `hat:umbrella` (the per-course umbrella glide art replaces the
beanie when picked). The garage reads `unlockedSet(record)`; `LOCKED` minus the unlocked karts is what
stays dark. Unlocks stay cosmetic only: never a stat, a feature or Sparkles. Sparkles come from
finishing, paid by place (B1): the host claims them from `race-result` and the server prices the claim.

## 7. Modes

`?mode=race` (default) / `?mode=trial`, also picked in the garage. Trial = the player alone (no bots,
no item boxes, no items), the local best ghost alongside, medals from `meta/medals.js`. Online is
always `race`. Sim-changing modes stay solo until the room's admission hash carries the mode.

## 8. Account record (SYNC)

CCP-Server `GET /v2/race/profile` -> `{ ok, record }`, `PUT /v2/race/profile` body `{ record }` ->
`{ ok, record }` (the merged result). Key `race:profile:<uid>` (JSON, 32 KB cap), server merge =
`mergeRecords` arithmetic, never lowers. The client merges the server record into the local one at
boot and PUTs after each saved run, through the same transport as the lobby (desktop host relay or
direct fetch on the web). Signed out: local only.

The record's optional `garage` block (7.2, shared shop contract C1) makes the kart look travel:
`{ state: <glassroad.garage.v1>, look: <glassroad.look.v1>, updatedAt: <ISO> }`. The newer `updatedAt` wins
the whole block, a side without one never erases it, a block over 4 KB is dropped alone. The page stamps it on a
real player change only (`hud/garageState.js garageTracker`, `hud/lobbyMath.js stampLook`), never on load, and
applies a newer one through `cleanGarage` while the garage is open and at rest, else at its next open. A pick
that does not fit falls back quietly and stays in the block for when the grant lands. `glassroad.garage.seen.v1`
remembers what this device last took. It never reaches feel.json or the tracks: no course hash move.

## 9. Copy

Dry, short, a little playful, plain. No em-dashes or en-dashes, no exclamation marks, gender neutral
toward the player.
