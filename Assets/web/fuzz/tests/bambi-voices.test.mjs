import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createBambiVoices } from '../ui/unlocks/themes-a/bambi-voices.js';

function scene() {
  let now = 0;
  const timers = new Set(), sounds = [], filters = [], loaded = [], events = new Map(), cleanup = [];
  const doc = Object.assign(new EventTarget(), { hidden:false, defaultView:new EventTarget() });
  const ctx = {
    world:{ ownerDocument:doc }, paused:false,
    audio:{
      play:(url, options) => sounds.push({ url, ...options, at:now }),
      muffle:(...args) => filters.push(args), preload:url => loaded.push(url),
    },
    after(delay, fn) {
      const timer = { at:now+delay, fn };
      timers.add(timer);
      return () => timers.delete(timer);
    },
    on:(type, fn) => events.set(type,fn), cleanup:fn => cleanup.push(fn),
  };
  const cues = createBambiVoices(ctx);
  function advance(to) {
    for (;;) {
      const next = [...timers].sort((a,b) => a.at-b.at)[0];
      if (!next || next.at>to) break;
      now=next.at; timers.delete(next); next.fn();
    }
    now=to;
  }
  return { ctx, doc, cues, sounds, filters, loaded, events, cleanup, advance };
}

test('first trusted click interrupts a giggle with the warm um, then sparse clicks cover the approved banks', () => {
  const s=scene(), { cues, sounds }=s, real={isTrusted:true};
  assert.match(s.loaded[0],/\/um\.mp3$/);
  assert.equal(cues.click({isTrusted:false},0),false);
  assert.equal(cues.giggle(0,'giggle1.mp3',.35),true);
  assert.equal(cues.click(real,.02),true);
  assert.equal(sounds[0].signal.aborted,true);
  assert.match(sounds[1].url,/\/um\.mp3$/);
  assert.equal(sounds[1].source,'voices');
  assert.equal(cues.giggle(.1,'giggle2.mp3',.35),false);
  for(let i=0;i<48;i++) assert.equal(cues.click(real,.03+i/30),false);
  assert.equal(sounds.length,2);
  for(let i=1;i<36;i++) cues.click(real, i*8);
  const names=new Set(sounds.map(s=>s.url.split('/').pop().replace('.mp3','')));
  for(const clip of ['um','so-like','umm-thinking','giggle-bimbo','giggle2','sink','dropping','feel-good','bimbo-doll','good-bimbo','soft','breathy','rising']) assert.ok(names.has(clip),clip);
  assert.ok(sounds.slice(0,-1).every(s=>s.signal.aborted));
  assert.ok(sounds.filter(s=>s.url.includes('/moans/')).every(s=>s.source==='moans'));
  cues.hush();assert.equal(sounds.at(-1).signal.aborted,true);
  assert.equal(cues.giggle(400,'giggle1.mp3',.35),false);
});

test('six interior cues use the shared lowpass on just the voice and moan channels, then stop before the trigger', () => {
  const {cues,sounds,filters,advance}=scene();
  cues.hush();cues.enter();cues.enter();
  advance(30000);
  assert.deepEqual(filters.map(f=>f[0]),sounds.map(s=>s.source));
  assert.ok(filters.every(f=>f[1] && f[2].hz===1250));
  assert.deepEqual(sounds.map(s=>s.at),[2200,6350,10650,16250,20300,24720]);
  assert.deepEqual(sounds.map(s=>s.source),['voices','moans','voices','voices','moans','voices']);
  cues.hush();cues.enter();advance(90000);
  assert.equal(sounds.length,6); assert.ok(sounds.every(s=>s.signal.aborted));
  assert.deepEqual(filters.slice(6),[['voices',false],['moans',false]]);
  cues.dispose();cues.dispose();assert.equal(filters.length,8);
});

test('hidden, pause, host focus, panic and disposal silence active clips and never queue clicks', () => {
  const {cues,sounds,ctx,doc,events,cleanup,advance}=scene();
  cues.click({isTrusted:true},0);
  doc.hidden=true;doc.dispatchEvent(new Event('visibilitychange'));assert.equal(sounds[0].signal.aborted,true);
  assert.equal(cues.click({isTrusted:true},9),false);
  doc.hidden=false;doc.defaultView.dispatchEvent(new Event('blur'));
  assert.equal(cues.click({isTrusted:true},10),false);
  doc.defaultView.dispatchEvent(new Event('focus'));cues.click({isTrusted:true},12);ctx.paused=true;events.get('pause')();
  assert.equal(sounds.at(-1).signal.aborted,true);
  assert.equal(cues.click({isTrusted:true},20),false);
  ctx.paused=false;cues.enter();advance(2300);
  events.get('suspend')({on:true,reason:'focus'});assert.equal(sounds.at(-1).signal.aborted,true);
  events.get('suspend')({on:true,reason:'panic'});cleanup.forEach(fn=>fn());
  advance(70000);assert.equal(sounds.length,3);
});
