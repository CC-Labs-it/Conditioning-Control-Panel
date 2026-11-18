# Dares: the wire (v1, 2026-11-18)

Bets between two friends on their own CCP use. Spec: `handoff-1118/DARES-HANDOFF.md` (owner session
2026-11-18). This file is the authority for both halves: `proxy/dares.js` + `proxy/dares-routes.js`
in CC-Labs-it/CCP-Server, the `/bet` bot command, and `Services/Dares/*` here.

## Door

Every op is `POST /v2/dares/<op>` with JSON `{ unified_id, ... }` and the account token in
`X-Auth-Token` (the friends door: `friends-routes.js gate()`, own meter `dares_rate:<uid>`, 60 a minute).
Replies are `{ ok: true, ... }` or `{ ok: false, reason }`. HTTP 200 for every worded reply, 401 bad
token, 429 `too_fast`, 503 no Redis.

DARK until `DARES_ENABLED`: `off` (default) answers every op `{ ok:false, reason:"off" }`; `allow`
opens it only to the unified ids in `DARES_ALLOW` (comma list; BOTH players of a bet must be on it,
else `create` answers `refused`); `on` opens it to everyone. Free for every tier.

## Grammar (refuse anything else with `{ ok:false, reason:"bad_input" }`)

- `type`: `cant` ("I bet [friend] can't [what] [by when]") or `duel` ("Me vs [friend]: who does more").
- `what` = `{ kind, target? }`:
  | kind | cant targets | duel | camera allowed |
  |---|---|---|---|
  | `pics` (pictures seen) | 100 250 500 1000 | yes, no target | yes |
  | `minutes` (minutes of use) | 30 60 120 | yes | yes |
  | `video` (video minutes) | 15 30 60 | yes | yes |
  | `session` (finish a session) | `easy` `medium` `hard` | no | yes |
  | `bubbles` (bubbles popped) | no | yes | no |
  | `dare` (custom dare, "do dare (n)") | target = dare number n | no | no |
- `deadline`: `3h` `today` `weekend` `week`. The server computes `ends_at` from the sender's
  `tz_min` (minutes east of UTC, -840..840): `3h` = now + 3 h; `today` = the sender's next local
  midnight (at least 1 h away, else +24 h); `weekend` = the coming Sunday 24:00 local (at least
  12 h away, else the one after); `week` = now + 7 days.
- `stake` = `{ kind, amount?, n? }`: `sp` amount 50 100 200 500; `lock` amount 10 20 30 (minutes on
  the loser's own Chaster lock); `lockdown` amount 10 20 30 (minutes of Lockdown); `dare` n = the
  dare number on this slip.
- `stakes` = `{ from, to }`: `from` is what the SENDER owes if the sender loses, `to` what the
  RECEIVER owes if the receiver loses. In `cant` the sender loses when the friend makes it; the
  receiver loses when they miss. In `duel` the lower count loses; equal counts = a draw, nobody owes.
- `dares` = `[ { n, by, text } ]` only on the server/bot side. `n` counts from 1 in order of
  appearance on the slip (what first, then the from stake, then the to stake); `by` = `from` | `to`
  (who has to do it). The desktop NEVER sends or receives `text` (Friends rule: no free text in the
  app). It shows "(n)" as a sealed dare and says the words are in Discord.
- `camera`: bool, only for kinds marked above; ignored (stored false) otherwise.

## States

`writing` (slip has dares, waiting for their words from Discord) -> `sent` -> `running` | `passed` |
`cancelled` | `lapsed` (no answer in 24 h). `running` -> `settled`. After settle the LOSER's side has
`debt.state`: `owed` -> `paid` | `renounced` | `claimed` (dare: the loser said they did it) ->
`paid` (the winner ticked it, or 48 h passed). `owed` for 7 days with no answer -> `renounced`
(owner call pending, default). A `lockdown` debt also has `serving` (started, not yet back).

Renounce (by press or by lapse) = no new dares for 7 days (`create` and `accept` answer `banned`
until `banned_until`), and the other side is told. Nothing else.

## Ops

| op | body | reply |
|---|---|---|
| `state` | `{ lockdown_ok? }` | `{ ok, me:{ banned_until, lockdown_ok, discord, sp_won_today }, bets:[B] }` |
| `create` | `{ to, slip, tz_min }` | `{ ok, status, id?, write_code? }` status: `sent` `writing` `not_friends` `banned` `friend_banned` `limit` `discord_needed` `lockdown_not_ok` `refused` |
| `accept` | `{ id }` | `{ ok, status }` status: `running` `gone` `banned` `limit` `lockdown_busy` |
| `pass` | `{ id }` | `{ ok }` |
| `cancel` | `{ id }` | `{ ok }` (sender, while `writing` or `sent`) |
| `report` | `{ id, count, seen_s?, counted_s? }` | `{ ok, bet:B }` |
| `pay` | `{ id, booked? }` | `{ ok, status, sp? }` status `paid` `gone` `refused`; `sp` = the payer's new balance after an `sp` debt (adopt it: a debited receipt may lower SkillPoints) |
| `start` | `{ id }` | `{ ok, status }` (`lockdown` debt -> `serving`) |
| `served` | `{ id, outcome }` | `{ ok, status }` outcome `served` -> `paid`; `ended_early` -> `owed` again the first time (one retry, `retry_used`), `paid` with `ended_early:true` the second |
| `did` | `{ id }` | `{ ok }` (`dare` debt -> `claimed`) |
| `tick` | `{ id }` | `{ ok }` (winner; `claimed` -> `paid`) |
| `renounce` | `{ id }` | `{ ok, banned_until }` |

`B` = `{ id, role:"from"|"to", friend:{ id, name, avatar }, type, what, deadline, ends_at, camera,
stakes:{ from, to }, dares:[{ n, by }], state, winner:"from"|"to"|null, counts:{ from, to },
camera_pass:{ from, to }, debt:{ state, retry_used, ended_early } | null, at, accepted_at,
settled_at, renounced_by:"from"|"to"|null }`. Newest first, open bets plus the last 20 finished.
Times are ISO strings from the server clock; the client reads them through `ServerClock`.

## Counting (client side, reported by `report`)

The client counts ONLY its own use from `accepted_at` to `ends_at` and reports a monotonic total on
every friends-poll tick while the bet runs (the server keeps the max). In `cant` only the receiver
reports; in `duel` both do.

- `pics`: flash images shown (one per image on screen, any mode).
- `minutes`: real minutes the engine or a session ran (`ConditioningClock`, never tick counts).
- `video`: real minutes a video played (HypnoTube or mandatory videos); several floating mandatory
  videos at once count ONCE (one clock, not one per window).
- `session`: 1 when a session of the named difficulty or harder completed.
- `bubbles`: bubbles popped.
- `dare` (what): 0 until the loser presses "I did it" (that goes through `did` after settle; a `cant`
  dare is settled at the deadline by the RECEIVER's own `report { count: 1 }` = "I did it").
- Camera: while a camera bet runs and its counting is live, the client samples presence every
  500 ms and sums `counted_s` (time counted) and `seen_s` (time a face was seen). Pass = seen >= 80%
  of counted. No picture leaves the PC; only the two numbers are sent.

Settlement: `cant` settles early when the receiver's count reaches the target with a camera pass
(winner = to), else at `ends_at` + 120 s grace (winner = from). `duel` settles at `ends_at` + 120 s:
a side failing the camera check counts 0; higher count wins; equal = draw (`winner:null`, no debt).
The server settles lazily on any `state` / `report` call by either side and on the bot cron.

## Sparkles (owner call 2026-11-18: Sparkles move player to player HERE ONLY)

An `sp` debt is paid by `pay`: the loser's netSp at `user.backroom.dares` goes down by the amount,
the winner's goes up by the amount capped at 500 Sparkles won through dares per UTC day
(`sp_won_today`); past the cap the loser pays only what the winner can still take. Like stakes, it
sits under `user.backroom` so `/v2/user/sync` reads it as spend and merge-accounts carries it.

## Limits

5 open bets per account (writing + sent + running + owed), 10 creates a UTC day, 1 owed or serving
`lockdown` debt at a time (`lockdown_busy`), offers lapse after 24 h. `lockdown` stakes need
`lockdown_ok` on the side that would owe it (sticky true once the client reports the player finished
a Lockdown of their own). A `dare` anywhere on a slip needs the sender's Discord linked
(`discord_needed`); its words are written with `/bet write code:<write_code>` (6 chars, 30 min).

## Discord

`/bet @friend` builds the same slip in Discord (both players need Discord linked to their CCP
account); the bot posts the slip in the channel where it was typed with Accept / Pass, then the
result in the same place. Nothing is staked before Accept. A bet made in the app posts nothing
public; the bot DMs both players each dare's number, who does it and its words.
