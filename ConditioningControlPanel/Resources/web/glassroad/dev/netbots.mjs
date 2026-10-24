/* dev/netbots.mjs - node-only players for the local net e2e (B2): each one signs in to the LOCAL lobby with the
 * dev fake identity, opens / joins / quick-matches a table, connects to the room and drives the real sim headless
 * (no browser, no three.js): the AI's hands on its own kart, the bot seats the room hands it, items online, pause.
 * Never point it at production.
 *
 *   node dev/netbots.mjs --lobby http://127.0.0.1:8811 --n 3 --mode quick
 *   node dev/netbots.mjs --lobby http://127.0.0.1:8811 --n 3 --mode open --seats 5 --laps 3 --start
 *   node dev/netbots.mjs --lobby http://127.0.0.1:8811 --n 2 --mode join --room <roomId> --pause 20
 * Flags: --uid netbot (uid prefix), --auth "Bearer jwt-" (header prefix, the race-local fake identity),
 *        --platform web|desktop|phone, --track threshold, --pause S (the first bot pauses 5 s at race second S),
 *        --quiet. Prints one JSON line per notable event and a summary line per bot; exit 0 when all finished.
 * import { runNetBot } for the e2e driver.
 */
import { readFileSync } from 'node:fs';
import { pathToFileURL } from 'node:url';
import { createFeel } from '../core/feel.js';
import { createActions } from '../core/actions.js';
import { loadTrack } from '../track/load.js';
import { courseHash } from '../net/courseHash.js';
import { createRace } from '../sim/race.js';
import { createRacers } from '../sim/racers.js';
import { createAi } from '../sim/ai.js';
import { createAssist } from '../sim/assist.js';
import { createContact } from '../sim/contact.js';
import { createStandings } from '../sim/standings.js';
import { createRoomSession } from '../net/room.js';
import { createLobbyApi, fetchTransport } from '../net/lobbyApi.js';
import { LOOK_OUTFITS, LOOK_VEHICLES } from '../net/protocol.js';

const ROOT = new URL('../', import.meta.url);
const DT = 1 / 60;

/** One node player. @returns Promise of { uid, seat, place, timeMs, finished, rows } */
export async function runNetBot({ lobbyBase, uid, auth = 'Bearer jwt-', mode = 'quick', roomId = null, open = null,
  track: trackId = 'threshold', platform = 'web', pauseAtS = -1, start = false, quiet = false, out = console.log, onSeated = null } = {}) {
  const say = (ev, o = {}) => { if (!quiet || ev === 'summary' || ev === 'error') out(JSON.stringify({ bot: uid, ev, ...o })); };
  const feel = createFeel(JSON.parse(readFileSync(new URL('feel.json', ROOT), 'utf8')));
  const trackJson = JSON.parse(readFileSync(new URL(`tracks/${trackId}.json`, ROOT), 'utf8'));
  const track = loadTrack(trackJson);
  const hash = await courseHash(trackJson, feel.toJSON());
  const lobby = createLobbyApi(fetchTransport({ base: lobbyBase, headers: { authorization: auth + uid }, credentials: 'omit' }));
  const n = [...uid].reduce((a, c) => a + c.charCodeAt(0), 0);
  const look = { outfit: LOOK_OUTFITS[n % LOOK_OUTFITS.length], vehicle: LOOK_VEHICLES[n % LOOK_VEHICLES.length] };

  let j;
  if (mode === 'open') j = await lobby.open({ track: trackId, mode: 'open', seats: 4, laps: 3, botFill: true, laneKeeper: false, look, ...(open || {}) });
  else if (mode === 'join') j = await lobby.join({ roomId, look });
  else j = await lobby.quickMatch({ track: trackId, look });
  say('seated', { roomId: j.roomId, seat: j.seat });
  if (onSeated) onSeated(j);

  const s = createRoomSession({ track, feel, courseHash: hash, lobby, platform, log: (lv, m) => say('log', { lv, m }) });
  const queue = { emit(name, p) { if (name === 'item-hit' || name === 'item-fire') say(name, p.victim !== undefined ? { victim: p.victim, kind: p.kind, verdict: p.verdict } : { racer: p.racer, id: p.id }); } };
  const goS = feel.get('juice.countdownPulses') * 60 / feel.get('clock.bpm');
  let sim = null, loop = 0, done = null;
  const finished = new Promise((res) => { done = res; });

  s.on('seats', (m) => say('seats', { filled: m.seats.filter((x) => x.kind !== 'empty').length }));
  s.on('keep', (m) => say('keep', { seats: m.seats }));
  s.on('error', (m) => say('room-error', m));
  s.on('start', () => { say('start', { countAt: s.countAt, startAt: s.startAt }); build(); });
  s.on('finish', (m) => {
    const me = m.rows.find((r) => r.seat === s.seat) || {};
    say('finish', { rows: m.rows.map((r) => [r.place, r.seat, r.name, r.timeMs]) });
    done({ uid, seat: s.seat, place: me.place, timeMs: me.timeMs, finished: !!me.finished, rows: m.rows });
  });
  s.on('closed', (m) => { say('closed', m); done({ uid, seat: s.seat, finished: false, closed: m.code }); });

  function build() {
    if (sim) return;
    const q = s.tap(queue);
    const race = createRace(track, feel, q);
    if (s.cfg && s.cfg.laps > 0) track.laps = s.cfg.laps;   // the sim reads track.laps; the room judges cfg.laps
    const racers = createRacers(track, feel, q, { net: s.layout(), seed: s.seed });
    const ai = createAi(track, feel, { seed: s.seed + s.seat });
    const assist = createAssist(track, feel);
    const contact = createContact(track, feel, q);
    const standings = createStandings(track, feel, q);
    const items = s.mount(racers, race, q);
    const a = Object.assign(createActions(), { item: false, back: false }), idle = Object.assign(createActions(), { item: false, back: false });
    sim = { race, racers, ai, assist, contact, standings, items, a, idle, acc: 0, last: Date.now(), started: false, paused: false };
  }

  function frame() {
    const now = Date.now();
    if (sim) {
      const m = sim;
      if (!m.started && s.serverNow() >= s.raceStartAt(goS)) { m.started = true; m.race.start(); }
      m.acc = Math.min(0.25, m.acc + (now - m.last) / 1000); m.last = now;
      const P = m.racers.player;
      while (m.started && m.acc >= DT) {
        m.acc -= DT;
        const pausedNow = pauseAtS >= 0 && m.race.timeS >= pauseAtS && m.race.timeS < pauseAtS + 5;
        if (pausedNow !== m.paused) { m.paused = pausedNow; say(pausedNow ? 'pause' : 'resume', { t: Math.round(m.race.timeS) }); }
        m.ai.drive(P, { racers: m.racers.list, items: m.items, standings: m.standings, race: m.race }, DT, m.a);
        const hands = m.paused ? m.assist.apply(m.idle, P.st, DT, m.race.phase) : m.a;   // pause: the assist coasts
        m.race.update(DT, hands, P.st);
        m.racers.step(DT, hands, m.race, { ai: m.ai, items: m.items, standings: m.standings });
        m.contact.update(DT, m.racers.list, m.race);
        m.standings.update(m.racers.list, m.race, DT);
        m.items.update(DT, m.racers.list, m.standings, m.race);
      }
      s.update({ paused: m.paused });
    } else s.update();
  }

  await s.connect(j, { lookNow: look });
  say('welcome', { seat: s.seat, phase: s.phase });
  s.ready(true);
  if (start && j.seat === 0) setTimeout(() => s.start(), 1500);
  loop = setInterval(frame, 8);
  const res = await finished;
  clearInterval(loop);
  setTimeout(() => s.leave(), 500);
  say('summary', res);
  return { roomId: j.roomId, ...res };
}

function args(argv) {
  const o = {};
  for (let i = 0; i < argv.length; i++) {
    const k = argv[i];
    if (!k.startsWith('--')) continue;
    const v = argv[i + 1] && !argv[i + 1].startsWith('--') ? argv[++i] : true;
    o[k.slice(2)] = v;
  }
  return o;
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const o = args(process.argv.slice(2));
  const base = String(o.lobby || 'http://127.0.0.1:8811');
  if (!/^http:\/\/(127\.0\.0\.1|localhost)(:\d+)?$/.test(base)) { console.error('netbots: local lobbies only'); process.exit(2); }
  const n = Math.max(1, Math.min(7, Number(o.n) || 3)), mode = String(o.mode || 'quick');
  const common = { lobbyBase: base, auth: o.auth || 'Bearer jwt-', track: String(o.track || 'threshold'), platform: String(o.platform || 'web'), quiet: !!o.quiet };
  const runs = [];
  let roomId = o.room || null;
  for (let i = 0; i < n; i++) {
    const uid = `${o.uid || 'netbot'}-${i + 1}`;
    const first = mode === 'open' && i === 0;
    let seated = null;
    const got = new Promise((res) => { seated = res; });
    const p = runNetBot({ ...common, uid, mode: first ? 'open' : mode === 'open' ? 'join' : mode, roomId, onSeated: (j) => seated(j.roomId),
      open: first ? { seats: Number(o.seats) || 4, laps: Number(o.laps) || 3 } : null,
      start: first && !!o.start, pauseAtS: i === 0 && o.pause !== undefined ? Number(o.pause) : -1 });
    runs.push(p);
    if (first) {
      roomId = await Promise.race([got, p.then(() => null, () => null)]);   // the others join the room it opened
      if (!roomId) break;
    }
  }
  const all = await Promise.allSettled(runs);
  const ok = all.every((x) => x.status === 'fulfilled' && x.value.finished);
  for (const x of all) if (x.status === 'rejected') console.log(JSON.stringify({ ev: 'error', error: String(x.reason && x.reason.message) }));
  process.exit(ok ? 0 : 1);
}
