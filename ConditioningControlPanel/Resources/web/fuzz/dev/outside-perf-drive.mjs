// Run: node dev/outside-perf-drive.mjs. Muted headless Chrome, no server or asset writes.
// Route a local-host replica directly from disk so separate process/network sessions cannot serve stale files.
// Covers atomic terrain streaming, abandoned-ring cleanup, async snapshot pause/resume and panic cancellation.
import {createRequire}from'node:module';import{readFileSync}from'node:fs';
const{chromium}=createRequire(process.env.PLAYWRIGHT_HOME || 'C:/wt-fe/e2e/')('playwright');
const browser=await chromium.launch({channel:'chrome',headless:true,args:['--mute-audio']});
try{
const page=await browser.newPage({viewport:{width:390,height:844},hasTouch:true});const errors=[];page.on('pageerror',e=>errors.push(String(e)));
await page.route('http://localhost:8897/**',r=>{const p=new URL(r.request().url()).pathname;try{r.fulfill({body:readFileSync(new URL('../../'+p.slice(1),import.meta.url)),contentType:p.endsWith('.js')?'text/javascript':p.endsWith('.json')?'application/json':p.endsWith('.png')?'image/png':'text/html'});}catch{r.fulfill({status:404,body:'missing'});}});
await page.route('**/probe.html',r=>r.fulfill({contentType:'text/html',body:'<style>body{margin:0}#host{position:fixed;inset:0}</style><script type="importmap">{"imports":{"three":"/vendor/three/three.module.min.js"}}</script><div id="host"></div>'}));
await page.goto('http://localhost:8897/fuzz/probe.html');
const queue=await page.evaluate(async()=>{
 const{createMeadow}=await import('/fuzz/ui/unlocks/extras/outside-meadow.js');const host=document.querySelector('#host');let step;
 const m=await createMeadow(host,{frame:f=>(step=f,()=>{}),atlasUrl:'/fuzz/assets/outside/meadow-atlas.png',atlasJsonUrl:'/fuzz/assets/outside/meadow-atlas.json'});
 m.start();dispatchEvent(new KeyboardEvent('keydown',{code:'KeyW'}));let samples=[];
 for(let i=0;i<30&&!m.debug().pendingChunks;i++)step(.05);
 const cancelledBefore=m.debug();if(!cancelledBefore.pendingChunks)throw Error('cancellation never staged');
 dispatchEvent(new KeyboardEvent('keyup',{code:'KeyW'}));dispatchEvent(new KeyboardEvent('keydown',{code:'KeyS'}));step(.05);
 const cancelledAfter=m.debug();if(cancelledAfter.pendingChunks||cancelledAfter.readyChunks||cancelledAfter.ringZ!==8)throw Error('obsolete ring retained');
 dispatchEvent(new KeyboardEvent('keyup',{code:'KeyS'}));dispatchEvent(new KeyboardEvent('keydown',{code:'KeyW'}));
 for(let i=0;i<80;i++){step(.05);const d=m.debug();samples.push({i,z:d.z,ringZ:d.ringZ,pending:d.pendingChunks,ready:d.readyChunks,quads:d.quads});}
 dispatchEvent(new KeyboardEvent('keyup',{code:'KeyW'}));const pending=samples.filter(x=>x.pending>0);
 if(!pending.length)throw Error('did not cross a chunk');
 for(const p of pending){if(p.ringZ!==8)throw Error('horizon changed before ring complete');if(p.quads!==samples[0].quads)throw Error('visible chunks changed while pending');}
 const complete=samples.find(x=>x.ringZ===7);if(!complete)throw Error('ring never committed');
 const blob=await m.stillBlob();if(!(blob instanceof Blob)||blob.type!=='image/jpeg')throw Error('async snapshot failed');
 m.stop();
 const midway=await createMeadow(host,{frame:f=>(step=f,()=>{}),atlasUrl:'/fuzz/assets/outside/meadow-atlas.png',atlasJsonUrl:'/fuzz/assets/outside/meadow-atlas.json'});
 midway.start();dispatchEvent(new KeyboardEvent('keydown',{code:'KeyW'}));for(let i=0;i<30&&!midway.debug().pendingChunks;i++)step(.05);
 if(!midway.debug().readyChunks)throw Error('midway disposal did not stage geometry');
 midway.stop();if(midway.debug().pendingChunks||midway.debug().readyChunks)throw Error('staged geometry retained after stop');
 return{cancelled:true,pending,complete,disposedMidway:true,blobBytes:blob.size,remaining:host.querySelectorAll('canvas,[data-field]').length};
});console.log(JSON.stringify({queue,errors}));
for(const cancel of [false,true]){
const capture=await page.evaluate(async(cancel)=>{
 const{outsideShow}=await import('/fuzz/ui/unlocks/extras/outside-show.js');const root=document.querySelector('#host');const frames=new Set(),events=new Map();let resolve,requested=false,paused=false,stops=0;
 const ctx={layer:()=>root,get paused(){return paused;},audio:{solo(){},input(){return null;},stream(){return null;},muffle(){}},brand:{text:s=>s},el(tag,css,text){const e=document.createElement(tag);e.style.cssText=css;if(text)e.textContent=text;root.append(e);return e;},frame(f){frames.add(f);return()=>frames.delete(f);},every(){return()=>{};},after(){},cleanup(){},on(t,f){if(!events.has(t))events.set(t,[]);events.get(t).push(f);},anim:(e,k,o)=>e.animate(k,o)};
 const ending=outsideShow(ctx,{credits:[],result:'Result',closing:'Closing'},{createMeadow:async(host)=>{const canvas=document.createElement('canvas');canvas.width=320;canvas.height=180;host.append(canvas);return{canvas,start(){},resize(){},lockControls(){},autopilot(){},stop(){canvas.remove();stops++;},stillBlob(){requested=true;return new Promise(r=>resolve=r);}};}});
 let show;for(let i=0;i<100;i++){show=root.querySelector('[data-outside="show"]');if(show?.__outside.ready())break;await new Promise(r=>setTimeout(r,25));}
 const tick=s=>{if(!paused)for(const f of [...frames])f(s);};for(let i=0;i<50&&!requested;i++)tick(1);
 if(!requested)throw Error('fallback capture did not start');
 paused=true;
 if(cancel)for(const f of events.get('suspend')||[])f({on:true,reason:'panic'});
 const c=document.createElement('canvas');c.width=32;c.height=32;const blob=await new Promise(r=>c.toBlob(r,'image/jpeg'));resolve(blob);await new Promise(r=>setTimeout(r,150));
 const before=root.querySelectorAll('[data-meadow="cover"]').length;if(before)throw Error('late/paused snapshot mounted');
 if(!cancel){paused=false;tick(.016);if(!root.querySelector('[data-meadow="cover"]'))throw Error('snapshot missing after resume');for(const f of events.get('suspend')||[])f({on:true,reason:'panic'});}
 const completed=await ending;return{cancel,completed,stops,remaining:root.childElementCount};
},cancel);console.log(JSON.stringify({capture,errors}));if(capture.completed||capture.remaining||errors.length)throw Error('capture lifecycle failed');
}
}finally{await browser.close();}
