import assert from 'node:assert/strict';
import test from 'node:test';
import {createWordRibbon} from '../ui/unlocks/basics/word-ribbon.js';

for (const hz of [30, 60, 144]) {
  test('words coast along the emitted path and turn in sequence at '+hz+' Hz', () => {
    const ribbon = createWordRibbon(6);
    ribbon.layout([44,42,36,44,44,40]);
    const frames = [];
    for (let frame=0; frame<=3*hz; frame++) {
      const t = frame/hz;
      const y = 650-520*Math.min(t,.75)+520*Math.min(.75,Math.max(0,t-1.05));
      ribbon.move(600,y,t*1000);
      ribbon.advance(t*1000);
      frames.push(ribbon.links.map(({x,y,opacity})=>({x,y,opacity})));
    }
    const at = t=>frames[Math.round(t*hz)];
    assert(at(.75)[0].y > 395, 'lead word is still well behind the hand');
    assert(at(1.04)[0].y < at(.9)[0].y-24, 'lead word keeps moving during a pause');
    const turns = [0,1,2].map(i=>frames.findIndex((f,n)=>
      n>1.05*hz && f[i].y>frames[n-1][i].y+.01)/hz);
    assert(turns[0]>1.23 && turns[0]<1.45);
    assert(turns[1]>turns[0]+.06 && turns[2]>turns[1]+.06);
    assert(frames.every(f=>f.every(link=>link.x===600 && link.y>=260 && link.y<=650)),
      'no word swings away from the vertical particle path');
    assert(at(2.55)[0].y > at(2.3)[0].y+8 && at(2.55)[0].opacity < at(2.3)[0].opacity);
    assert.equal(ribbon.stroke,1);
    assert.equal(ribbon.alive(3000),false);
  });
}

test('short strokes show one word, sustained strokes spread the tail, and a fresh nudge is short again',()=>{
  const ribbon=createWordRibbon(6);
  const visible=()=>ribbon.links.filter(link=>link.opacity>.1);
  function sweep(start,length,duration) {
    for(let frame=0;frame<=duration/10;frame++){
      const t=frame*10;
      ribbon.move(90+length*t/duration,100,start+t);
      ribbon.advance(start+t);
    }
    ribbon.advance(start+duration+150);
    return visible();
  }
  assert.equal(sweep(0,36,190).length,1);
  const long=sweep(1500,650,1000);
  assert(long.length>=5);
  assert(long[0].x-long.at(-1).x>220);
  assert.equal(sweep(4000,36,190).length,1);
});
