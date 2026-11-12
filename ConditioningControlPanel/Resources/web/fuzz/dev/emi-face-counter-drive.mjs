import {mkdirSync} from 'node:fs';
import {resolve} from 'node:path';
const out=resolve(process.argv[2]||'dev/shots/emi');mkdirSync(out,{recursive:true});
import {createRequire} from 'node:module';
import assert from 'node:assert/strict';
const {chromium}=createRequire('C:/wt-fe/e2e/package.json')('playwright');
const b=await chromium.launch({channel:'chrome',headless:true,args:['--mute-audio']});
try{
 const p=await b.newPage({viewport:{width:1100,height:850}}),errors=[];p.on('pageerror',e=>errors.push(e.message));
 await p.goto('http://localhost:8807/fuzz/index.html?fresh=1&buy=em_head,quiz,counter');await p.waitForSelector('.xe[data-emi] .xe-f canvas');
 await p.waitForTimeout(2200);
 await p.evaluate(()=>{fuzz.engine.scopes.get('quiz').ret.close();});await p.waitForTimeout(600);
 assert.equal(await p.locator('.xe-f canvas').count(),1,'Questions uses the existing face');
 {
   const selector='.xe[data-emi]';
   const e=p.locator(selector),f=e.locator('.xe-f'),c=f.locator('canvas');assert.equal(await c.getAttribute('width'),'152');assert.equal(await c.getAttribute('height'),'137');
   await p.mouse.move(1090,400);await p.waitForTimeout(700);const right=await c.evaluate(e=>new DOMMatrix(getComputedStyle(e).transform).m41);
   await p.mouse.move(0,400);await p.waitForTimeout(700);const left=await c.evaluate(e=>new DOMMatrix(getComputedStyle(e).transform).m41);
   assert(right>0&&left<0,`${selector}: face looks left and right (${left},${right})`);
   assert(await c.evaluate(e=>e.getContext('2d').getImageData(0,0,e.width,e.height).data.some((v,i)=>i%4===3&&v)));
   await e.screenshot({path:`${out}/emi-canonical.png`});
 }
 await p.evaluate(()=>fuzz.engine.setPaused(true));const face=p.locator('.xe[data-emi] .xe-f canvas');const frozen=await face.evaluate(e=>e.style.transform);await p.mouse.move(1090,800);await p.waitForTimeout(250);assert.equal(await face.evaluate(e=>e.style.transform),frozen);
 await p.evaluate(()=>{fuzz.engine.setPaused(false);fuzz.ui.setInit({gentle:true});});await p.waitForTimeout(100);assert.equal(await face.evaluate(e=>new DOMMatrix(getComputedStyle(e).transform).m41),0);
 await p.evaluate(()=>{fuzz.ui.setInit({gentle:false});const e=fuzz.engine;window.step=e.step.bind(e);e.step=()=>{};});
 for(const n of [999,1000,9999,10000,62574,0,15,6,90000,60000]){
  await p.evaluate(n=>{fuzz.engine.run.fuzz=n;step(75);},n);await p.waitForTimeout(20);
  const leading=await p.locator('#fz-roll > .d').first().evaluate(e=>({glyph:e.textContent,children:e.childElementCount,motion:e.getAnimations().length}));
  assert.equal(leading.children,0);assert.match(leading.glyph,/^\d$/);assert.equal(leading.motion,0,'first digit lands without a second glyph or movement');
  await p.waitForTimeout(340);
 }
 assert.deepEqual(errors,[]);console.log('Passed: canonical face pixels, the shared mascot follows the pointer, pause, Gentle, leading digit growth/shrink/spending transitions.');
}finally{await b.close();}
