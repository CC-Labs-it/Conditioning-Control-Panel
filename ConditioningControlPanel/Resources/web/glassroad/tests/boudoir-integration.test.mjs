import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { loadTrack } from '../track/load.js';
import { getBoudoirLayout } from '../track/boudoir-layout.js';
import { S, RESPAWN } from '../sim/state.js';
import { step } from '../sim/kart.js';
import { glideActive } from '../sim/glide.js';
import { makeFeel, place, acts, recorder, DT } from './sim-fake.mjs';
import { allowsOnline } from '../track/selection.js';
const track = loadTrack(JSON.parse(readFileSync(new URL('../tracks/boudoir.json', import.meta.url),'utf8')));
const layout = getBoudoirLayout(track), ramp = layout.mirrorJump;
function flight(speed, held) {
  const st = place(track,{s:ramp.rampS0-30,speed}), feel=makeFeel(), events=recorder();
  let launched=false,active=0;
  for(let tick=0;tick<900;tick++) {
    step(st,acts({throttle:1,hop:held}),track,feel,DT,events);
    if(st[S.airborne])launched=true;
    if(glideActive(st))active++;
    assert.equal(st[S.respawnPhase],RESPAWN.NONE,'mirror flight does not need rescue');
    if(launched&&!st[S.airborne])return {s:st[S.s],active};
  }
  assert.fail('mirror flight never landed');
}
for(const speed of [12,28,40])test(`Boudoir mirror launch lands with and without held rotor at ${speed}m/s`,()=>{
  const plain=flight(speed,false),held=flight(speed,true);
  assert.ok(plain.s>=ramp.landingS0 && plain.s<ramp.landingS1,'ordinary jump has a real landing');
  assert.ok(held.s>=ramp.landingS0 && held.s<ramp.landingS1,'held rotor lands before next turn');
  assert.ok(held.active>15,'held jump activates the cap');
  assert.ok(held.s>plain.s+5,'holding jump materially extends flight');
});
test('Boudoir remains a local course until a matching room card is admitted',()=>{
  assert.equal(allowsOnline('boudoir'),false);
});
