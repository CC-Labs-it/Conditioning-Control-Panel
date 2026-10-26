import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';

const source = readFileSync(new URL('../boot.js', import.meta.url), 'utf8');
function fn(name) {
  const start = source.indexOf(`  function ${name}(`);
  assert.ok(start >= 0, `missing lifecycle function ${name}`);
  return source.slice(start, source.indexOf('\n  }', start) + 4);
}
function run(extra = '') {
  const events = [];
  const sandbox = { events, Promise, console };
  vm.runInNewContext(`
    let state='menu', paused=false, online=null, introElapsed=0, introTarget=null, raceAudioStart=null;
    const lifetime={alive:true}, pickedSong=null, CHARTS=['course-chart'], DEV=false;
    const course={}, track={id:'mainframe'}, trackName='Mainframe', laps=3, P={view:[],st:[]}, S={lap:0};
    const renderer={aspect:16/9}, canvas={focus(){events.push('focus')}}, introHud={style:{visibility:''}};
    const world={setLook(){events.push('look')},looks(){return ['look']}}, debug={};
    const cam={snap(){events.push('snap')},camera:{clone(){return {copy(){}}}}};
    const garage={state:{},setVisible(on){events.push(['garage',on])}};
    const audio={start(){events.push('audio-unlock');return Promise.resolve()},setPaused(on){events.push(['audio-paused',on])}};
    const clock={pause(on){events.push(['clock-paused',on])}}, bus={emit(name,data){events.push([name,data])}};
    const standings={placeOf(){return 1}},racers={list:Array(8)},hud={setCard(kind){events.push(['card',kind])}};
    const race={phase:'ready',goInS:3,start(){events.push('countdown');this.phase='countdown'}};
    const queue={flush(){events.push('queue')}},bridge={runStarted(data){events.push(['host-start',data])}};
    function resetAll(){introElapsed=0;introTarget=null;events.push('reset')}
    function unpause(){paused=false}
    function raceLook(){return 'look'}
    function syncAssist(){events.push('assist')}
    function playSong(align){events.push(['song',align()])}
    function stopSong(){events.push('song-stop')}
    function openRunLog(){} function closeRunLog(){}
    function prepareRunMode(){}
    ${fn('begin')}
    ${fn('startRace')}
    ${fn('setPaused')}
    ${extra}
    globalThis.api={startRace,begin,setPaused,read:()=>({state,paused,introElapsed,hidden:introHud.style.visibility}),
      online(on){online=on?{}:null},die(){lifetime.alive=false}};
  `, sandbox);
  return { ...sandbox.api, events };
}

test('solo Race enters the course tour without countdown or a music voice', async () => {
  const r=run(); r.startRace(); await Promise.resolve();
  assert.equal(r.read().state,'intro');
  assert.equal(r.read().hidden,'hidden');
  assert.ok(!r.events.includes('countdown'));
  assert.ok(!r.events.some(e=>Array.isArray(e)&&e[0]==='song'));
  assert.equal(r.events.filter(e=>Array.isArray(e)&&e[0]==='host-start').length,1);
});

test('tour completion starts countdown and schedules music at GO', async () => {
  const r=run(); r.startRace(); r.begin(); await Promise.resolve();
  assert.equal(r.read().state,'race');
  assert.equal(r.read().hidden,'');
  assert.equal(r.events.filter(e=>e==='countdown').length,1);
  assert.ok(r.events.some(e=>Array.isArray(e)&&e[0]==='song'&&e[1]===-3));
  assert.equal(r.events.filter(e=>Array.isArray(e)&&e[0]==='host-start').length,1);
});

test('an intro can pause and resume using the same solo clock and audio gates', () => {
  const r=run(); r.startRace(); r.setPaused(true);
  assert.equal(r.read().state,'intro'); assert.equal(r.read().paused,true);
  assert.ok(r.events.some(e=>Array.isArray(e)&&e[0]==='clock-paused'&&e[1]));
  assert.ok(r.events.some(e=>Array.isArray(e)&&e[0]==='card'&&e[1]==='pause'));
  r.setPaused(false); assert.equal(r.read().paused,false);
  assert.ok(!r.events.includes('countdown'));
});

test('solo replay resets the tour and refuses to override an online room start', () => {
  const r=run(); r.startRace(); r.startRace();
  assert.equal(r.read().introElapsed,0);
  assert.equal(r.events.filter(e=>e==='reset').length,2);
  r.online(true); const before=r.events.length; r.startRace();
  assert.equal(r.events.length,before);
});

test('a disposed course cannot start another tour', () => {
  const r=run();r.die();r.startRace();assert.equal(r.events.length,0);
});
