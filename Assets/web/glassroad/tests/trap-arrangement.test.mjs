import test from 'node:test';
import assert from 'node:assert/strict';
import { normalizeChart } from '../audio/chart.js';
import { trapArrangement, trapStroke, trapSwing } from '../track/trapArrangement.js';
import { setMachineryBeat, machineryBeatAt, hazardBeatAt } from '../track/machineryBeat.js';
import { movingPose } from '../track/movingHazards.js';
import { keepGatePhase } from '../sim/keepGates.js';
import { industryPose } from '../sim/mainframeIndustry.js';
import { goonworksPose } from '../track/goonworks-setpieces.js';
import { mascaraPhase } from '../track/boudoir-setpieces.js';
import { createPressContacts } from '../render/pressContacts.js';
const chart = more => normalizeChart({ id:'test-song', bpm:96, durationS:80, ...more });
function setup(song = chart(), id = 'threshold') {
  const track = {id};
  setMachineryBeat(track, {enabled:true, song, bpm:song.bpm, songS:0, raceS:0});
  return {track, frame:b => machineryBeatAt(track,b*60/song.bpm)};
}
const row = {offsetS:0, offsetBeats:0, x:10, side:1, travelM:10};

test('machinery cadence is half the old subdivisions, with unchanged heard beat anchoring', () => {
  const {frame} = setup(); assert.equal(frame(8).beat,8); assert.equal(frame(8).beatS,.625);
});
test('five course motifs differ and sections create longer breaks and high-point responses', () => {
  const song=chart(), names=['threshold','keep','mainframe','boudoir','goonworks'];
  const patterns=names.map(n=>trapArrangement(song,n).closes);
  assert.notDeepEqual(patterns[0],patterns[1]); assert.notDeepEqual(patterns[1],patterns[2]);
  assert.notDeepEqual(patterns[2],patterns[3]); assert.notDeepEqual(patterns[3],patterns[4]);
  const quiet=trapArrangement(chart({sections:[{startS:0,endS:80,mood:'break'}]})).closes;
  const high=trapArrangement(chart({sections:[{startS:0,endS:80,mood:'peak'}]})).closes;
  assert.ok(quiet.length < patterns[0].length/2); assert.notDeepEqual(high,patterns[0]);
  assert.equal(trapArrangement(song),trapArrangement(song));
});
test('marked words and drops replace routine strikes, with enough spacing for strategy', () => {
  const song=chart({triggers:[{startS:6*.625,kind:'word',w:'test'},{startS:7*.625,kind:'drop'},{startS:16*.625,kind:'drop'}]});
  const {closes}=trapArrangement(song);
  assert.ok(closes.includes(6)); assert.ok(closes.includes(16)); assert.ok(!closes.includes(7));
  assert.ok(!closes.includes(4)); assert.ok(!closes.includes(12));
  assert.notDeepEqual(closes,trapArrangement(chart()).closes);
  const sectionDrop=chart({sections:[{startS:10,endS:80,mood:'drop'}]});
  assert.ok(trapArrangement(sectionDrop).closes.includes(16),'the section drop itself anchors a hit');
  for(let i=1;i<closes.length;i++) assert.ok(closes[i]-closes[i-1]>=6);
});
test('a real stroke has full-beat travel, two-beat lingering and a longer fully retracted window', () => {
  const {frame}=setup();
  const pose=b=>trapStroke(row,frame(b));
  assert.equal(pose(0).extension,0); assert.equal(pose(1.1).warning,true);
  assert.equal(pose(2.9).extension,0); assert.ok(pose(3.5).extension>.49);
  for(const b of [4,4.5,5,5.99]) assert.equal(pose(b).extension,1);
  assert.ok(pose(6.5).extension<.51);
  for(const b of [7,8,9,10.9]) assert.equal(pose(b).extension,0);
  for(let b=0;b<32;b+=.013) assert.ok(Math.abs(pose(b+1e-6).extension-pose(b).extension)<.00001);
});
test('horizontal, gate, piston and rising toys draw the same slow collision stroke', () => {
  const {frame}=setup();
  for(let b=0;b<32;b+=.13) {
    const f=frame(b), p=trapStroke(row,f), gate=keepGatePhase(row,0,{},f), press=industryPose({...row,kind:'piston'},0,f);
    assert.equal(movingPose(row,0,f).extension,p.extension);
    assert.equal(gate.liftM,8.4*(1-p.extension)); assert.equal(press.lift,7*(1-p.extension));
    for(const kind of ['block','flipper']) assert.equal(goonworksPose({...row,kind,height:4,depth:14},0,f).height,4*p.extension);
  }
});
test('fast recordings retain the marked hit and at least 400 ms to move without overlapping strokes', () => {
  const song=chart({bpm:240,triggers:[{startS:5,kind:'drop'}]}), {frame}=setup(song);
  assert.ok(trapArrangement(song).closes.includes(20));
  const p=b=>trapStroke(row,frame(b));
  assert.equal(p(18).extension,0); assert.ok(p(19).extension>.49); assert.equal(p(20).extension,1);
  const closes=trapArrangement(song).closes;
  for(let i=1;i<closes.length;i++) assert.ok(closes[i]-closes[i-1]>=12);
});
test('seek, song replacement, pause and resumed transport cannot skip the full warning', () => {
  const song=chart(), {track}=setup(song);
  setMachineryBeat(track,{enabled:true,song,bpm:96,songS:2.5,raceS:2.5,playing:false});
  assert.deepEqual(trapStroke(row,machineryBeatAt(track,2.5)),trapStroke(row,machineryBeatAt(track,100)));
  setMachineryBeat(track,{enabled:true,song,bpm:96,songS:25,raceS:2.6});
  for(let t=2.6;t<5;t+=.1) assert.equal(trapStroke(row,machineryBeatAt(track,t)).extension,0);
  setMachineryBeat(track,{enabled:true,song:chart({id:'new'}),bpm:96,songS:25,raceS:2.7});
  assert.equal(machineryBeatAt(track,2.7).safe,true);
  setMachineryBeat(track,null); assert.equal(machineryBeatAt(track,3),null);
  assert.equal(hazardBeatAt(track,3).beat,6);
});
test('pendulums linger at both ends with exact velocities and continuous seek release', () => {
  for(const b of [.5,1.5,4.5,5.5]) assert.equal(trapSwing(b,.625).speed,0);
  const {frame}=setup(), brush={x:0,offsetS:0,armM:8.6,pivotY:10.25,amplitudeM:2.9};
  for(let b=0;b<16;b+=.17) {
    const eps=.00001, p=mascaraPhase(brush,0,frame(b));
    const a=mascaraPhase(brush,0,frame(b-eps/.625)), z=mascaraPhase(brush,0,frame(b+eps/.625));
    assert.ok(Math.abs((z.x-a.x)/(2*eps)-p.vx)<.0001);
    assert.ok(Math.abs((z.y-a.y)/(2*eps)-p.vy)<.0001);
  }
});
test('slower press contacts emit once at the audible closure and remain quiet throughout the hold', () => {
  const {track}=setup(), events=[], zone={...row,id:'press',kind:'piston',p:[0,0,0],b:[1,0,0],x:0};
  const contacts=createPressContacts(track,(...args)=>events.push(args),[zone]);
  for(let b=3.8;b<6;b+=.05) contacts.update(b*.625,[0,0,0]);
  assert.equal(events.length,1);
});
