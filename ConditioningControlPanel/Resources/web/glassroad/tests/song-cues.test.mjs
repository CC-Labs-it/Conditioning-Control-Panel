import test from 'node:test';
import assert from 'node:assert/strict';
import { normalizeChart } from '../audio/chart.js';
import { resolveCueAnchor, songCues, createSongCueRun } from '../sim/songCues.js';
import { createState, S, setPos, setVel } from '../sim/state.js';

const track = { lengthM: 10000, frameAt(s, out = {}) { return Object.assign(out, { p: [0, 0, s], b: [1, 0, 0], n: [0, 1, 0], t: [0, 0, 1], w: 20 }); } };
const kart = (s, v = 30, x = 0, y = 0) => { const st = createState(); setPos(st, [x,y,s]); setVel(st, [0,0,v]); st[S.s] = s; return st; };
const raw = (mode = 'CROSS') => ({ durationS: 30, words: [{ w: 'signal', startS: 6 }, { w: 'signal', startS: 12 }], events: [{ event: 'hit', startS: 15 }],
  cues: [{ id: 'a', kind: mode === 'CROSS' ? 'VEIL' : 'MARQUEE', mode, anchor: { word: 'signal', n: 1 }, durationS: 4 }] });
const song = mode => normalizeChart(raw(mode));
const drive = (run, end = 10, fps = 60, v = 30) => { const fired = []; for (let i = 0; i <= end * fps; i++) { const t=i/fps; for(const r of run.update(t,kart(200+v*t,v)).fired) fired.push([t,r.startS]); } return fired; };

test('anchors are exact, one-based, punctuation tolerant and reject unresolved sources', () => {
  const c = song(); c.words.push({ w: 'clear', startS: 17 }, { w: 'channel.', startS: 18 });
  assert.equal(resolveCueAnchor(c, { word: 'SIGNAL.', n: 2, offsetS: .2 }), 12.2);
  assert.equal(resolveCueAnchor(c, { word: 'clear channel', n: 1 }), 17);
  assert.equal(resolveCueAnchor(c, { event: 'hit', n: 1 }), 15);
  for (const a of [{word:'missing',n:1},{word:'signal',n:0},{event:'missing',n:1},{word:'signal',event:'hit',n:1},{word:'signal',n:1,offsetS:NaN}]) assert.equal(resolveCueAnchor(c,a),null);
  c.cues.push({...c.cues[0],id:'bad',endAnchor:{event:'missing',n:1}}, {...c.cues[0]});
  assert.equal(songCues(c).length,1);
});
test('crossings align at all required speeds and frame rates without changing kart state', () => {
  for (const fps of [30,60,120]) for(const v of [14,30,44]) {
    const run=createSongCueRun(song(),track), hits=drive(run,10,fps,v);
    assert.equal(hits.length,1); assert.ok(Math.abs(hits[0][0]-hits[0][1])<=1/fps+.001);
    const st=kart(500), before=st.slice(); run.update(10.1,st); assert.deepEqual(st,before);
  }
});
test('cross planes lock before impact; watch spots settle on the road, stay put and get driven past', () => {
  const cross=createSongCueRun(song(),track), watch=createSongCueRun(song('WATCH'),track); let lock, spot, passed=false;
  for(let i=0;i<12*60;i++) {
    const t=i/60, st=kart(200+30*t);
    const row=cross.update(t,st).live[0]; if(row?.locked) { lock ??= row.s; assert.equal(row.s,lock); }
    const seen=watch.update(t,st).live[0];
    if(seen?.locked) {
      spot ??= seen.s; assert.equal(seen.s,spot);   // fixed in the world: never rides along with the kart
      if(t<=6) assert.ok(seen.s>st[S.s]+30);        // the drop lands well ahead of the kart
    }
    passed ||= seen?.state==='passed';
  }
  // Hanging screens stay three seconds ahead at impact so the full face fits the approach view.
  assert.ok(Math.abs(spot-(200+30*9))<1); assert.ok(passed); assert.equal(watch.live.length,0);
});
test('a re-dropping rig drops a fresh fixed screen on each of its beats', () => {
  const c=song('WATCH'); c.cues[0].params={redrop:true}; c.cues[0].jolts=[{word:'signal',n:2}]; c.cues[0].endAnchor={event:'hit',n:1};
  const run=createSongCueRun(c,track); assert.deepEqual(run.cues.map(x=>[x.id,x.startS]),[['a',6],['a~1',12]]);
  const spots=new Map();
  for(let i=0;i<16*60;i++){const t=i/60;for(const r of run.update(t,kart(200+30*t)).live) if(r.locked) spots.set(r.id,r.s);}
  assert.ok(Math.abs(spots.get('a')-(200+30*9))<1); assert.ok(Math.abs(spots.get('a~1')-(200+30*15))<1);
});
test('sky and zone pieces need no road spot and survive a stopped kart', () => {
  const c=song('WATCH'); c.cues[0].kind='SHIFT'; const run=createSongCueRun(c,track);
  for(let i=0;i<8*60;i++){const t=i/60,st=kart(300,t>5?0:30);run.update(t,st);}
  assert.equal(run.live.length,1); assert.equal(run.live[0].state,'hit'); assert.equal(run.live[0].ahead,0);
});
test('stops, reverse, off-road, upper roads, rescue, teleport and late seek cannot fire crossings', () => {
  for(const mode of ['stop','reverse','edge','upper','rescue','teleport','seek']) {
    const run=createSongCueRun(song(),track); let hits=0, faded=false;
    for(let i=0;i<=9*60;i++) {
      const t=i/60; if(mode==='seek'&&t>5&&t<7) continue;
      const st=kart(200+30*t,mode==='reverse'?-30:30,mode==='edge'?30:0,mode==='upper'?12:0);
      if(t>5.6) { if(mode==='stop'){setVel(st,[0,0,0]);setPos(st,[0,0,368]);st[S.s]=368;} if(mode==='rescue')st[S.respawnPhase]=1; if(mode==='teleport'){st[S.posZ]+=100;st[S.s]+=100;} }
      const r=run.update(t,st); hits+=r.fired.length; faded ||= r.live.some(x=>x.state==='miss'&&x.alpha>0);
    }
    assert.equal(hits,0,mode); assert.equal(run.live.length,0,mode);
    if(['stop','rescue','teleport'].includes(mode))assert.ok(faded,mode);
  }
});
test('pause freezes, backward seek/loop rearm, restart and dispose clear all rows', () => {
  const run=createSongCueRun(song(),track); run.update(4,kart(320));run.update(4.1,kart(323));
  const before=JSON.stringify(run.live);run.update(5,kart(1000),{paused:true});assert.equal(JSON.stringify(run.live),before);
  assert.equal(drive(run).length,1);assert.equal(drive(run).length,1);
  run.reset();assert.equal(run.live.length,0); assert.equal(drive(run).length,1);
  run.dispose();assert.equal(drive(run).length,0);assert.equal(run.live.length,0);
});
test('stacked cues all fire and unresolved ends/jolts never invent moments', () => {
  const c=song('WATCH');c.cues[0].jolts=[{event:'hit',n:1},{event:'absent',n:1}];c.cues[0].endAnchor={event:'hit',n:1};
  c.cues.push({...c.cues[0],id:'b'}); const run=createSongCueRun(c,track);
  assert.equal(drive(run).length,2);assert.deepEqual(run.cues[0].joltTimes,[15]);
});
test('fork placement uses physical distance and fades if a locked crossing changes branch', () => {
  const fork={id:'fork',lengthM:1000,toS:8000,localS:s=>(s-1000)/4,mainS:s=>1000+s*4,
    frameAt(s,out={}){track.frameAt(s,out);out.p[0]=100;return out;}};
  const branched={...track,forkAtSeg:seg=>seg===999?fork:null,pathFrameAt:(s,seg,out)=>seg===999?fork.frameAt(fork.localS(s),out):track.frameAt(s,out)};
  for(const leave of [false,true]) { const run=createSongCueRun(song(),branched);let hits=0;
    for(let i=0;i<8*60;i++){const t=i/60,z=100+30*t,st=kart(z,30,100);st[S.seg]=999;st[S.s]=fork.mainS(z);if(leave&&t>5.8)st[S.seg]=0;hits+=run.update(t,st).fired.length;}
    assert.equal(hits,leave?0:1);
  }
});
test('a locked plane just beyond a fork exit accepts the continuous forward crossing', () => {
  const fork={id:'exit',lengthM:100,toS:1100,localS:s=>s-1000,mainS:s=>s+1000,frameAt:(s,out)=>track.frameAt(s+1000,out)};
  const path={...track,forkAtSeg:seg=>seg===999?fork:null,pathFrameAt:(s,seg,out)=>track.frameAt(s,out)};
  const run=createSongCueRun(song(),path);let hits=0;
  for(let i=0;i<8*60;i++){const t=i/60,s=923+30*t,st=kart(s);st[S.seg]=s<1100?999:0;hits+=run.update(t,st).fired.length;}
  assert.equal(hits,1);
});
test('seek reconstructs ongoing WATCH holds without firing their impacts', () => {
  const run=createSongCueRun(song('WATCH'),track);run.update(1,kart(230));
  const result=run.update(8,kart(440));assert.equal(result.live.length,1);assert.equal(result.fired.length,0);
  assert.equal(result.live[0].state,'hit');run.update(8.1,kart(443));assert.ok(run.live[0].alpha>0);
});

test('dropping screens retain readable approach distance at slow and fast pace, including seeks', () => {
  for (const speed of [14, 30, 65]) {
    const c = song('WATCH'); c.cues[0].params = { standoffS: .2 };
    const run = createSongCueRun(c, track);
    let screen;
    for (let i = 0; i <= 360; i++) screen = run.update(i / 60, kart(200 + speed * i / 60, speed)).live[0];
    assert.ok(screen.s - (200 + speed * 6) >= Math.max(60, speed * 3) - 1);
    const seek = createSongCueRun(c, track);
    seek.update(1, kart(200, speed));
    const resumed = seek.update(8, kart(400, speed)).live[0];
    assert.ok(resumed.s - 400 >= Math.max(60, speed * 3) - 1);
  }
});
