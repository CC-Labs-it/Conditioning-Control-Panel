import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { loadTrack } from '../track/load.js';
import { createAssist } from '../sim/assist.js';
import { S } from '../sim/state.js';
import { place, makeFeel, acts, DT } from './sim-fake.mjs';

const track = loadTrack(JSON.parse(readFileSync(new URL('../tracks/threshold.json', import.meta.url), 'utf8')));

test('the hat is a fully hands-off choice, immediately returned when the player touches a control', () => {
  {  // Light (Help) never drives hands off, so it never holds the hat either
    const feel = makeFeel(), assist = createAssist(track, feel, { mode: 'light' });
    const st = place(track, { s: track.aerials[0].rampS0 + 3, speed: 22 });
    let out;
    for (let i = 0; i < 600; i++) out = assist.apply(acts(), st, DT, 'racing');
    assert.equal(assist.view.auto, 0); assert.equal(assist.view.cruise, 0);
    assert.equal(out.hop, false, 'light: the hat stays the player\'s');
  }
  for (const mode of ['strong']) {
    const feel = makeFeel(), assist = createAssist(track, feel, { mode });
    const st = place(track, { s: track.aerials[0].rampS0 + 3, speed: 22 });
    let out;
    for (let i = 0; i < 600; i++) out = assist.apply(acts(), st, DT, 'racing');
    assert.ok(assist.view.auto >= .99 && assist.view.cruise >= .99);
    assert.equal(out.hop, true);
    out = assist.apply(acts({ throttle: 1 }), st, DT, 'racing');
    assert.equal(out.hop, false, 'holding throttle leaves flight controls to the player');
    assert.equal(assist.view.cruise, 0);
    for (let i = 0; i < 600; i++) out = assist.apply(acts(), st, DT, 'racing');
    out = assist.apply(acts({ steer: .3 }), st, DT, 'racing');
    assert.equal(out.hop, false); assert.equal(assist.view.auto, 0);
    assert.ok(out.steer >= .3);
    st[S.s] = 25;
    for (let i = 0; i < 600; i++) out = assist.apply(acts(), st, DT, 'racing');
    assert.equal(out.hop, false, 'ordinary road never acquires a hat hold');
  }
});
