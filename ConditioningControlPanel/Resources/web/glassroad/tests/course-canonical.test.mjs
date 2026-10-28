import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { loadTrack } from '../track/load.js';
import { movingPlan } from '../track/movingHazards.js';

for (const id of ['threshold','keep','mainframe','boudoir','goonworks']) {
  test(id+': actual road and moving geometry survive cross-engine ULP differences canonically', () => {
    const source = JSON.parse(readFileSync(new URL('../tracks/'+id+'.json',import.meta.url),'utf8'));
    const before = JSON.stringify(source), track = loadTrack(source,{moving:true});
    assert.equal(JSON.stringify(source),before,'canonical construction never changes authored JSON');
    for (const p of track.courseShapeNodes) for (const value of p) assert.equal(value,Math.round(value*1e6)/1e6,'physical spline input uses common micrometre coordinates');
    const perturb = { ...track, frameAt(s,out) {
      const f=track.frameAt(s,out);
      for (const axis of ['b','n','t']) for (let k=0;k<3;k++) {
        if (Math.abs(f[axis][k])>1e-14) f[axis][k]+=Math.sign(f[axis][k])*Number.EPSILON;
      }
      return f;
    } };
    assert.equal(JSON.stringify(movingPlan(perturb)),JSON.stringify(movingPlan(track)),
      'the actual physical trap plan, not just its hash, ignores engine-sized frame noise');
    const inspect = value => {
      if (typeof value==='number' && Number.isFinite(value)) {
        assert.equal(value,Math.round(value*1e9)/1e9,'installed trap and bridge geometry remains canonical');
      } else if (value && typeof value==='object') for (const child of Object.values(value)) inspect(child);
    };
    inspect(track.moving);
  });
}
