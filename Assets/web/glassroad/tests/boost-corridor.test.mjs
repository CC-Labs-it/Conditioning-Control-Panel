import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { loadTrack } from '../track/load.js';
import { safeBoostCorridor } from '../track/boostCorridor.js';
import { BOOST_TURN } from '../track/boostSafety.js';
const read = id => JSON.parse(readFileSync(new URL('../tracks/'+id+'.json',import.meta.url),'utf8'));

for (const id of ['threshold','keep','mainframe','boudoir','goonworks']) {
  test(id+': every painted boost lane stays supported through the actual complete carry and braking runout', () => {
    const track = loadTrack(read(id), { moving:true }), L = track.lengthM;
    assert.ok(track.boosts.length >= 6, 'every course retains useful boosts');
    // Different stations and lateral samples from the implementation, including the exact endpoint.
    for (const pad of track.boosts) {
      const end = pad.len+BOOST_TURN.rolloutM;
      const distances = Array.from({length:Math.ceil(end/7)},(_,i) => i*7).concat(end);
      for (const d of distances) for (const fraction of [-.5,-.25,0,.25,.5]) {
        const s = (pad.s+d)%L, x = (pad.x||0)+fraction*pad.w;
        const f = track.surfaceAt(s,x), q = track.query(...f.p,f.seg);
        const ds = Math.abs(q.s-s);
        assert.ok(q.onRoad && q.railed && Math.min(ds,L-ds)<.5 && Math.abs(q.height)<.2,
          `pad ${pad.s} at ${d}m, lane ${x} must have its own guarded physical support`);
        assert.ok(q.edgeDist >= 1.99, 'painted pad footprint retains a two-metre shoulder');
      }
    }
  });
}

test('Threshold pre-island pads cannot force full solo boost into the manual stepping field', () => {
  const source = read('threshold'), track = loadTrack(source,{moving:true,boostSafety:false});
  const pad = track.boosts.find(p => p.node===36 && p.x===-3);
  assert.ok(pad, 'actual original authored regression pad exists');
  assert.ok(!safeBoostCorridor(track,pad), 'its formerly unchecked >60m carry reaches a real void');
  const field = track.fields.find(f => f.id==='bridge-stones'); assert.ok(field);
  let unsupported = false;
  for (let s=field.fromS;s<field.toS;s+=1) {
    if (s-pad.s<60 || s-pad.s>BOOST_TURN.forcedM) continue;
    const f=track.surfaceAt(s,pad.x),q=track.query(...f.p,f.seg);
    if (!q.onRoad) { unsupported=true; break; }
  }
  assert.ok(unsupported,'independent probe finds an actual unsupported forced-carry station');
});
