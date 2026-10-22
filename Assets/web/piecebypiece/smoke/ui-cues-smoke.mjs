/* ============================================================================
 * smoke/ui-cues-smoke.mjs - the front door's sounds (audio/ui-cues.js), in node:
 *
 *   node smoke/ui-cues-smoke.mjs
 *
 * The plan: every cue in the contract has voices, quiet and short (the house
 * caps), every pitched note of a pitched cue sits in C5 pentatonic, variants
 * differ, the hover tune walks. The player, on a fake context: a hover or an
 * open never makes the context (no gesture), a press does; hover and tick are
 * rate-limited; volume 0 and a hidden tab play nothing; setVolume reaches the
 * output; every call is a no-op without a context and never throws.
 * ==========================================================================*/

import { uiCuePlan, createUiCues, CUE_NAMES, UI_SOUND, pentaHz, HOVER_RUNGS, ui } from '../audio/ui-cues.js';

let passed = 0;
const failures = [];
const ok = (what, cond) => { if (cond) passed++; else failures.push(what); };

/* ---------------------------------------------------------------- the plan */
const CONTRACT = ['hover', 'press', 'open', 'back', 'select', 'tick', 'stamp', 'error'];
ok('every contract name is a cue', CONTRACT.every((n) => CUE_NAMES.includes(n)));
ok('the default instance has every name and setVolume', [...CONTRACT, 'setVolume'].every((n) => typeof ui[n] === 'function'));
const PENTA_HZ = Array.from({ length: 25 }, (_, i) => pentaHz(i - 10));
const inKey = (hz) => PENTA_HZ.some((p) => Math.abs(1200 * Math.log2(hz / p)) < 50);   // within the 2.5 percent jitter
for (const n of CUE_NAMES) {
  const p = uiCuePlan(n, { rand: () => 0.5 });
  ok(`${n} has voices`, p.length > 0);
  ok(`${n} is quiet (<= ${UI_SOUND.maxGain})`, p.every((v) => v.gain > 0 && v.gain <= UI_SOUND.maxGain));
  ok(`${n} is short (<= ${UI_SOUND.maxSec}s)`, p.every((v) => v.at + v.sec <= UI_SOUND.maxSec));
  ok(`${n} only uses soft waves`, p.every((v) => ['sine', 'triangle', 'noise'].includes(v.wave)));
}
// pitched notes (not the knocks, pops and thuds that slide) sit in the one key
for (const n of ['hover', 'open', 'back', 'tick', 'error']) {
  const notes = uiCuePlan(n, { rand: () => 0.5 }).filter((v) => v.wave !== 'noise' && v.gain >= 0.02);
  ok(`${n}: every note is C5 pentatonic`, notes.every((v) => inKey(v.hz)));
}
ok('select and stamp chime in key', ['select', 'stamp'].every((n) => uiCuePlan(n, { rand: () => 0.5 }).filter((v) => v.at > 0).every((v) => v.wave === 'noise' || inKey(v.hz))));
ok('hover walks a tune', new Set(HOVER_RUNGS.map((_, i) => uiCuePlan('hover', { step: i })[0].hz)).size >= 4);
ok('tick climbs', uiCuePlan('tick', { step: 4 })[0].hz > uiCuePlan('tick', { step: 0 })[0].hz);
ok('a press varies', uiCuePlan('press', { rand: () => 0 })[0].hz !== uiCuePlan('press', { rand: () => 1 })[0].hz);
ok('back is softer than open', Math.max(...uiCuePlan('back').map((v) => v.gain)) < Math.max(...uiCuePlan('open').map((v) => v.gain)));
ok('an unknown cue is silent', uiCuePlan('nope').length === 0);

/* -------------------------------------------------------------- the player */
function fakeContext() {
  const param = () => ({ value: 0, setValueAtTime() {}, exponentialRampToValueAtTime() {}, linearRampToValueAtTime() {}, setTargetAtTime(v) { this.value = v; } });
  const node = (extra = {}) => ({ connect() {}, disconnect() {}, ...extra });
  const c = {
    state: 'running', currentTime: 0, sampleRate: 8000, destination: node(), started: 0, made: 0,
    createGain: () => node({ gain: param() }),
    createOscillator: () => node({ type: 'sine', frequency: param(), start() { c.started++; }, stop() {} }),
    createBufferSource: () => node({ buffer: null, loop: false, start() { c.started++; }, stop() {} }),
    createBiquadFilter: () => node({ type: 'lowpass', frequency: param() }),
    createBuffer: (ch, len) => ({ getChannelData: () => new Float32Array(len) }),
    resume() { c.state = 'running'; return Promise.resolve(); },
  };
  return c;
}
{
  let t = 1000;
  let made = null;
  const doc = { hidden: false, addEventListener() {} };
  const win = { PBP: { settings: { sfxVolume: 0.6 } } };
  const u = createUiCues({ context: () => (made = fakeContext()), now: () => t, doc, win, rand: () => 0.5 });
  u.hover(); u.open(); u.tick(); u.stamp();
  ok('hover / open / tick / stamp never make the context', made === null);
  u.press();
  ok('a press makes the context and plays', made && made.started === 2);
  const before = made.started;
  t += 2000; u.hover(); t += 10; u.hover();
  ok('hover is rate-limited', made.started === before + 2);
  t += UI_SOUND.hoverGapMs + 1; u.hover();
  ok('a hover after the gap plays', made.started === before + 4);
  const steps = u.log().filter((e) => e.name === 'hover').map((e) => e.step);
  ok('a sweep steps the tune, a pause restarts it', steps.join(',') === '0,1');
  t += UI_SOUND.hoverRunMs + 1; u.hover();
  ok('after a pause the tune starts over', u.log().at(-1).step === 0);
  t += 100; u.tick(); t += 5; u.tick();
  ok('tick is rate-limited', u.log().filter((e) => e.name === 'tick').length === 1);
  u.setVolume(0); const n0 = made.started; t += 100; u.press(); u.open(); u.stamp();
  ok('volume 0 plays nothing', made.started === n0);
  u.setVolume(0.4); doc.hidden = true; t += 100; u.press();
  ok('a hidden tab plays nothing', made.started === n0);
  doc.hidden = false; t += 100; u.select(); u.select();
  const sel = u.log().filter((e) => e.name === 'select').map((e) => e.step);
  ok('select rotates its chime', sel.length === 2 && sel[0] !== sel[1]);
  ok('volume is what was set', u.volume() === 0.4);
}
{
  // no AudioContext anywhere: every call is a quiet no-op
  const u = createUiCues({ doc: null, win: {} });
  let threw = false;
  try { for (const n of CONTRACT) u[n](); u.setVolume(2); u.setVolume(NaN); } catch { threw = true; }
  ok('no context: nothing throws', !threw && !u.ready());
  ok('setVolume clamps', u.volume() === 0);
}

if (failures.length) {
  console.error(`ui-cues smoke: ${failures.length} FAILED, ${passed} passed`);
  for (const f of failures) console.error('  x ' + f);
  process.exit(1);
}
console.log(`ui-cues smoke: ${passed} passed`);
