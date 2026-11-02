/* meta/runLog.js - the run recorder (CONTRACT-LOGBOOK.md sections 2 and 3).
 *
 * createRunLog(bus, opts) listens to the main bus for one run and finish(result) returns RunSummary v1.
 * Untagged events are the player's; an event tagged `racer` counts only for kart 0; bots arrive wrapped as
 * racer-event and are never read. sample(st, place, dt) runs once a frame while racing and allocates nothing.
 * Optional extras on the summary (contract: new fields are optional): winMarginS (seconds to 2nd when the player
 * won, 0 otherwise) and lengthM (the course length, for the recap sketch).
 */
import { S } from '../sim/state.js';

const FALLS_MAX = 20;
const mine = (p) => !p || typeof p.racer !== 'number' || p.racer === 0;
const r2 = (x) => Math.round(x * 100) / 100;
const TRAP_EVENTS = Object.freeze({ 'keep-gate-hit': 'keepGate', 'keep-chain-hit': 'keepChain', 'moving-trap-hit': 'moving',
  'goonworks-hit': 'goonworks', 'scan-hit': 'scan', 'perfume-contact': 'perfume', 'industry-hit': 'industry' });
const TRICK_RANK = Object.freeze({ good: 1, great: 2 });

/** The room's result rows ({ place, seat, finished, timeMs, bestLapMs }) as a race-result payload for finish(). */
export function roomResult(rows, seat) {
  const list = (Array.isArray(rows) ? rows : []).map((r) => ({ place: r.place | 0, me: r.seat === seat, finished: !!r.finished,
    timeS: r.timeMs > 0 ? r.timeMs / 1000 : 0, bestLapS: r.bestLapMs > 0 ? r.bestLapMs / 1000 : 0 }));
  const me = list.find((r) => r.me);
  return { rows: list, me: me ? { place: me.place, timeS: me.timeS, bestLapS: me.bestLapS } : null };
}

/**
 * @param {{on: Function}} bus the main event bus
 * @param {{track: string, mode?: string, online?: boolean, laps?: number, seed?: number, assist?: string,
 *   gentle?: boolean, lengthM?: number, offered?: () => number}} o
 *   offered: optional getter for the words shown this run (read at finish)
 */
export function createRunLog(bus, o = {}) {
  const L = o.lengthM > 0 ? o.lengthM : 0;
  const c = {
    driftStarts: 0, driftBoosts: [0, 0, 0], slipstreams: 0, pads: 0, airS: 0, longestAirS: 0, jumps: 0,
    tricks: 0, bestRank: 0, overtakes: 0, walls: 0, falls: 0, rescues: 0, fallsAtS: [], hitsTaken: 0,
    itemsUsed: 0, itemsLanded: 0, tetherBreaks: 0, discBankHits: 0, bigSpiralMax: 0,
    traps: { keepGate: 0, keepChain: 0, moving: 0, goonworks: 0, scan: 0, perfume: 0, industry: 0 },
    mirrorsBroken: 0, wordsCaught: 0, songGatesPassed: 0, songGatesOffered: 0, cleanLaps: 0,
  };
  const stations = new Set(), lapTimes = [], sectors = [], placeAtLap = [];
  let clockS = 0, lastS = -1, lastPlace = 0, topPct = 0, distanceM = 0, leadS = 0;
  let sector = 0, sectorStartS = 0, lapSectors = [0, 0, 0], lapDirty = false;
  let discBounced = false, bigCount = -1;

  const offs = [];
  const on = (name, fn) => offs.push(bus.on(name, fn));
  on('drift-start', () => { c.driftStarts++; });
  on('drift-boost', (p) => { const t = p.tier | 0; if (t >= 1 && t <= 3) c.driftBoosts[t - 1]++; });
  on('slipstream', (p) => { if (mine(p) && p.state === 'fire') c.slipstreams++; });
  on('pad', () => { c.pads++; });
  on('land', (p) => {
    const a = +p.airTime || 0;
    if (a < 0.25) return;
    c.jumps++; c.airS += a; if (a > c.longestAirS) c.longestAirS = a;
  });
  on('trick', () => { c.tricks++; });
  on('trick-land', (p) => { const k = TRICK_RANK[p.grade] || 0; if (k > c.bestRank) c.bestRank = k; });
  on('overtake', (p) => { if (p.racer === 0) c.overtakes++; });
  on('wall', () => { c.walls++; lapDirty = true; });
  on('drop', () => {
    c.falls++; lapDirty = true;
    if (c.fallsAtS.length < FALLS_MAX) c.fallsAtS.push(r2(lastS >= 0 ? lastS : 0));
  });
  on('rescue-release', () => { c.rescues++; });
  on('item-fire', (p) => {
    if (p.racer !== 0) return;
    c.itemsUsed++;
    if (p.id === 'disc') discBounced = false;
    if (p.id === 'bigSpiral') bigCount = 0;
  });
  on('disc-bounce', (p) => { if (p.by === 0) discBounced = true; });
  on('item-hit', (p) => {
    if (p.verdict !== 'hit') return;
    if (p.victim === 0) { c.hitsTaken++; lapDirty = true; return; }
    if (p.by !== 0) return;
    c.itemsLanded++;
    if (p.kind === 'disc' && discBounced) { c.discBankHits++; discBounced = false; }
    if (p.kind === 'bigSpiral' && bigCount >= 0) { bigCount++; if (bigCount > c.bigSpiralMax) c.bigSpiralMax = bigCount; }
  });
  on('tether-break', (p) => { if (p.by === 0 && p.target !== 0 && p.reason === 'pass') c.tetherBreaks++; });
  for (const [name, key] of Object.entries(TRAP_EVENTS)) on(name, (p) => { if (mine(p)) c.traps[key]++; });
  on('boudoir-mirror-break', (p) => {
    if (!mine(p)) return;
    c.mirrorsBroken++;
    const parts = String(p.id || '').split('-'), st = parts.length >= 3 ? parts[parts.length - 2] : p.id;
    stations.add(st);
  });
  on('word-pop', () => { c.wordsCaught++; });
  on('song-gate', (p) => { c.songGatesOffered++; if (p.passed) c.songGatesPassed++; });
  on('lap', (p) => {
    const t = +p.timeS || clockS;
    lapTimes.push(r2(+p.lapTimeS || 0));
    lapSectors[Math.min(2, sector)] = r2(Math.max(0, t - sectorStartS));
    sectors.push(lapSectors); lapSectors = [0, 0, 0];
    sector = 0; sectorStartS = t; clockS = t;
    placeAtLap.push(lastPlace);
    if (!lapDirty) c.cleanLaps++;
    lapDirty = false;
  });

  /** Once a frame while racing: the player's state, place and the frame's seconds. No allocation. */
  function sample(st, place, dt) {
    if (!st) return;
    const d = dt > 0 && dt < 1 ? dt : 0;
    clockS += d;
    if (place > 0) { lastPlace = place; if (place === 1) leadS += d; }
    const pct = st[S.speedPct];
    if (pct > topPct) topPct = pct;
    const s = st[S.s];
    if (L > 0) {
      if (lastS >= 0) {
        let ds = s - lastS;
        if (ds < -L / 2) ds += L; else if (ds > L / 2) ds -= L;
        if (ds > 0 && ds < 60) distanceM += ds;
      }
      const k = Math.min(2, Math.max(0, Math.floor((3 * s) / L)));
      if (k === sector + 1) { lapSectors[sector] = r2(clockS - sectorStartS); sector = k; sectorStartS = clockS; }
    }
    lastS = s;
  }

  /** @param {object} result the race-result payload (sim/standings.js buildResult), or null for a quit run */
  function finish(result = null) {
    const rows = (result && result.rows) || [];
    const me = rows.find((r) => r.me) || (result && result.me) || null;
    const finished = !!(me && me.finished !== false && result);
    const trial = o.mode === 'trial';
    let winMarginS = 0;
    if (me && me.place === 1 && rows.length > 1) {
      const second = rows.find((r) => r.place === 2);
      if (second && second.finished && second.timeS > me.timeS) winMarginS = r2(second.timeS - me.timeS);
    }
    const offered = Math.max(c.wordsCaught, (typeof o.offered === 'function' && +o.offered()) || 0);
    return {
      v: 1, track: o.track || '', mode: trial ? 'trial' : 'race', online: !!o.online,
      laps: o.laps | 0, seed: o.seed | 0, assist: o.assist === 'strong' || o.assist === 'off' ? o.assist : 'light',
      gentle: !!o.gentle, finished,
      place: trial ? 1 : (me && me.place) || 0, of: trial ? 1 : rows.length,
      timeS: me && me.timeS > 0 ? r2(me.timeS) : 0, bestLapS: me && me.bestLapS > 0 ? r2(me.bestLapS) : 0,
      lapTimesS: lapTimes.slice(), sectorsS: sectors.map((x) => x.slice()), placeAtLap: placeAtLap.slice(),
      ledEveryLap: placeAtLap.length > 0 && placeAtLap.every((p) => p === 1),
      leadS: r2(leadS), topSpeedPct: r2(topPct), distanceM: Math.round(distanceM),
      driftStarts: c.driftStarts, driftBoosts: c.driftBoosts.slice(), slipstreams: c.slipstreams, pads: c.pads,
      airS: r2(c.airS), longestAirS: r2(c.longestAirS), jumps: c.jumps,
      tricks: c.tricks, bestTrick: c.bestRank === 2 ? 'great' : c.bestRank === 1 ? 'good' : null,
      overtakes: c.overtakes, walls: c.walls, falls: c.falls, rescues: c.rescues, fallsAtS: c.fallsAtS.slice(),
      hitsTaken: c.hitsTaken, itemsUsed: c.itemsUsed, itemsLanded: c.itemsLanded,
      tetherBreaks: c.tetherBreaks, discBankHits: c.discBankHits, bigSpiralMax: c.bigSpiralMax,
      traps: { ...c.traps }, mirrorsBroken: c.mirrorsBroken, mirrorStations: stations.size,
      wordsCaught: c.wordsCaught, wordsOffered: offered,
      songGatesPassed: c.songGatesPassed, songGatesOffered: c.songGatesOffered,
      cleanLaps: c.cleanLaps, winMarginS, lengthM: Math.round(L),
      at: new Date().toISOString(),
    };
  }

  function dispose() { for (const off of offs.splice(0)) off(); }
  return { sample, finish, dispose };
}
