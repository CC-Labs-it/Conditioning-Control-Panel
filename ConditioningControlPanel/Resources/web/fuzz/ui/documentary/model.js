import { trackMotion, trackPitch, trackPoint } from './track.js';

export const SPEED_BANDS = Object.freeze([
  { label: 'Stopped', below: 0.15, reactionS: 4.5, cooldownS: 7 },
  { label: 'Crawl', below: 2, reactionS: 2.4, cooldownS: 7 },
  { label: 'Documentary pace', below: 5, reactionS: 9, cooldownS: 18 },
  { label: 'Brisk', below: 9, reactionS: 2.4, cooldownS: 7 },
  { label: 'Montage', below: Infinity, reactionS: 2.4, cooldownS: 7 },
]);

export const STORY_ART_SLOTS = Object.freeze([
  'intro-subject', 'pump-documentary', 'reflex-cheetah', 'reflex-shrimp',
  'subject-habitat', 'floorboard-warning', 'subject-spreadsheet',
  'outside-daylight', 'conversation-chairs',
]);

export function isDocumentaryArtSlot(slot) {
  return typeof slot === 'string' && (/^[blfws]-[a-z-]+$/.test(slot) || STORY_ART_SLOTS.includes(slot));
}

export function validateManifest(manifest) {
  if (manifest?.version !== 1 || !Number.isFinite(manifest.track?.dock) || manifest.track.dock <= 0 ||
      manifest.track.length < manifest.track.dock) throw new Error('Invalid documentary track');
  const ids = new Set();
  for (const item of [...manifest.track.zones, ...manifest.exhibits, ...manifest.banners]) {
    if (!item.id || ids.has(item.id)) throw new Error('Duplicate or missing documentary id');
    ids.add(item.id);
  }
  let end = 0;
  for (const zone of manifest.track.zones) {
    if (zone.from !== end || zone.to <= zone.from || (zone.cap != null && zone.cap <= 0)) {
      throw new Error('Documentary zones must cover the track in order');
    }
    end = zone.to;
  }
  if (end !== manifest.track.dock) throw new Error('Documentary zones must reach the dock');
  for (const exhibit of manifest.exhibits) {
    if (!['L', 'R', 'C', 'both'].includes(exhibit.side) || !Number.isFinite(exhibit.at) ||
        exhibit.at < 0 || exhibit.at > manifest.track.length) throw new Error('Invalid exhibit position');
    for (const slot of [exhibit.slot, exhibit.fallback, exhibit.stamp, exhibit.snapTo, ...Object.values(exhibit.states || {})]) {
      if (slot != null && !isDocumentaryArtSlot(slot)) throw new Error('Invalid art slot');
    }
  }
  return manifest;
}

/** Pure cart model. Event handlers may install a hold while a cue is crossed. */
export function createRide(manifest, { cues = [], reduced = false, at = 0 } = {}, emit = () => {}) {
  validateManifest(manifest);
  const dock = manifest.track.dock;
  const initial = at === 'stage' ? dock : typeof at === 'string' ? manifest.track.zones.find(zone => zone.id === at)?.from : at;
  let z = Math.max(0, Math.min(dock, Number(initial) || 0));
  let v = 0, locked = z < 12, docked = z === dock;
  let leverAngle = 0, leverTarget = 0, band = 0, heldS = 0, bandTick = 0;
  let zone = null, exhibit = null, started = false;
  const holds = new Map();
  const cueMap = new Map();
  const crossed = new Set();
  const state = Object.freeze({
    get z() { return z; }, get v() { return v; }, get locked() { return locked; },
    get docked() { return docked; }, get leverAngle() { return leverAngle; },
    get leverTarget() { return leverTarget; }, get band() { return band; },
    get zone() { return zone; }, get exhibit() { return exhibit; },
    get motion() { return trackMotion(z, reduced).mode; },
    get height() { return trackPoint(z).y; }, get pitch() { return trackPitch(z); },
    get holds() { return Object.freeze([...holds.keys()]); },
  });
  const stoppedByHold = () => [...holds.values()].some(stop => z >= stop.point - 0.0001);
  function addCues(items) {
    for (const cue of items) {
      if (!cue.id || !Number.isFinite(cue.at) || cue.at < 0 || cue.at > dock) throw new Error('Invalid documentary cue');
      const old = cueMap.get(cue.id);
      if (old && old.at !== cue.at) throw new Error('A cue id has two positions');
      cueMap.set(cue.id, Object.freeze({ ...cue }));
      if (cue.at < z) crossed.add(cue.id);
    }
  }
  addCues([...(manifest.cues || []), ...cues]);
  function updateLocation() {
    const nextZone = docked ? 'stage' : manifest.track.zones.find(item => z >= item.from && z < item.to)?.id;
    if (nextZone !== zone) {
      if (zone) emit('zone', { id: zone, enter: false });
      zone = nextZone;
      emit('zone', { id: zone, enter: true });
    }
    const nearest = manifest.exhibits.filter(item => item.at - z >= 1 && item.at - z <= 11)
      .sort((a, b) => a.at - b.at)[0];
    if ((nearest?.id || null) !== exhibit) {
      exhibit = nearest?.id || null;
      emit('exhibit', nearest ? { id: nearest.id, slot: nearest.slot } : null);
    }
  }
  function reportBand(dt) {
    const next = SPEED_BANDS.findIndex(item => v < item.below);
    heldS += dt;
    bandTick += dt;
    if (next !== band) {
      band = next;
      heldS = 0;
      bandTick = 1;
    }
    if (bandTick >= 1) {
      bandTick %= 1;
      emit('band', { band, label: SPEED_BANDS[band].label, heldS });
    }
  }
  function step(seconds) {
    if (!started) {
      started = true;
      updateLocation();
      emit('band', { band, label: SPEED_BANDS[band].label, heldS });
      if (docked) emit('arrive');
    }
    if (!Number.isFinite(seconds) || seconds <= 0) return;
    let remaining = seconds;
    while (remaining > 0.000001 && !docked) {
      const dt = Math.min(1 / 60, remaining);
      remaining -= dt;
      leverAngle += (leverTarget - leverAngle) * (1 - Math.exp(-12 * dt));
      const blocked = locked || stoppedByHold();
      if (blocked) v = 0;
      const currentZone = manifest.track.zones.find(item => z >= item.from && z < item.to);
      const motion = trackMotion(z, reduced);
      let cap = motion.cap;
      if (currentZone?.cap) cap = Math.min(cap, currentZone.cap);
      if (z >= 553) cap = Math.min(cap, Math.max(0.7, (dock - z) * 0.4));
      v = Math.min(v, cap);
      let nextV = Math.max(0, (v + 0.375) * Math.exp(-0.32 * dt) - 0.375);
      if (!blocked && motion.mode !== 'manual') {
        const gravity = motion.mode === 'descent' ? Math.max(0, -motion.grade) * (reduced ? 2 : 9.81) : 0;
        // Powered lifts crest without clicking; downhill momentum carries the connecting galleries.
        const drive = Math.max(0, Math.min(motion.acceleration, (motion.target - v) * 1.5));
        nextV = Math.min(cap, Math.max(0, v + (drive + gravity - .045 * v) * dt));
      }
      let nextZ = Math.min(dock, z + (v + nextV) * 0.5 * dt);
      v = nextV < 0.001 ? 0 : nextV;
      for (const { point } of holds.values()) nextZ = Math.min(nextZ, Math.max(z, point));
      const due = [...cueMap.values()].filter(cue => !crossed.has(cue.id) && cue.at >= z && cue.at <= nextZ)
        .sort((a, b) => a.at - b.at);
      for (const cue of due) {
        z = cue.at;
        crossed.add(cue.id);
        emit('cue', { ...cue });
        for (const { point } of holds.values()) nextZ = Math.min(nextZ, Math.max(z, point));
        if (nextZ <= z) break;
      }
      z = nextZ;
      if (stoppedByHold()) v = 0;
      if (z >= dock) { z = dock; v = 0; docked = true; }
      updateLocation();
      reportBand(dt);
      if (docked) emit('arrive');
    }
  }
  return {
    state, step, addCues,
    unlock() { locked = false; },
    pump() {
      const blocked = locked || docked || stoppedByHold();
      leverTarget = leverTarget > 0 ? -0.34 : 0.34;
      if (blocked) leverTarget *= 0.22;
      if (!blocked) v = Math.min(trackMotion(z, reduced).cap, v + (v < 1 ? 1.4 : 1));
      emit('pump', { v, locked: blocked });
    },
    hold(id, untilZ) {
      if (!id || !Number.isFinite(untilZ)) throw new Error('Invalid hold');
      const token = { point: Math.max(z, Math.min(dock, untilZ)) };
      holds.set(id, token);
      return () => { if (holds.get(id) === token) holds.delete(id); };
    },
  };
}
