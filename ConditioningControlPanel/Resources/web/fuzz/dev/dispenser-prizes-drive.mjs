import {mkdirSync} from 'node:fs';
import {resolve} from 'node:path';
const out=resolve(process.argv[2]||'dev/shots/prizes');mkdirSync(out,{recursive:true});
import {createRequire} from 'node:module';
import assert from 'node:assert/strict';
const {chromium}=createRequire('C:/wt-fe/e2e/package.json')('playwright');
const browser=await chromium.launch({channel:'chrome',headless:true,args:['--mute-audio']});
try{
 const p=await browser.newPage({viewport:{width:1100,height:850}}),errors=[];p.on('pageerror',e=>errors.push(e.message));
 await p.goto('http://localhost:8807/fuzz/index.html?fresh=1&buy=dispenser,trail,a_hum&pictures=none');
 await p.waitForFunction(()=>document.querySelector('.prize-crank')&&!document.querySelector('.prize-crank').disabled);
 await p.evaluate(()=>{const e=fuzz.engine;window.advance=e.step.bind(e);e.step=()=>{};window.go=ms=>{while(ms>0){const d=Math.min(50,ms);advance(d);ms-=d;}};});
 const root=p.locator('.prize-machine'),canvas=p.locator('.prize-art');
 for(const color of ['red','cyan','purple','gold']){
   await p.evaluate(()=>go(31000));
   await p.locator('.prize-crank').click();
   await p.evaluate(color=>{fuzz.engine.run.store.dispenser.machine.prize=color;go(3250);},color);
   await root.screenshot({path:`${out}/prize-${color}-drop.png`});
   await p.evaluate(()=>go(700));assert.equal(await root.getAttribute('data-phase'),'catch');
   assert.equal(await canvas.getAttribute('data-prize'),color);
   await root.screenshot({path:`${out}/prize-${color}-ready.png`});
   const before=await p.evaluate(()=>fuzz.state.sources.dispenser||0);
   await p.locator('.prize-bubble').click();await p.evaluate(()=>go(450));
   const got=await p.evaluate(before=>(fuzz.state.sources.dispenser||0)-before,before);assert(got>0);
   assert.equal(await p.locator('[data-dispenser-effects]').getAttribute('data-reward'),color);
   await p.screenshot({path:`${out}/prize-${color}-burst.png`});
   await root.screenshot({path:`${out}/prize-${color}-open.png`});
   console.log(color,got);
 }
 await p.evaluate(()=>go(6000));
 for(let i=0;i<75;i++){await p.mouse.move(320+i*6,430+Math.sin(i/12)*48);await p.evaluate(()=>go(6));}
 assert(Number(await p.locator('[data-cursor-trail]').getAttribute('data-particles'))>100);
 await p.screenshot({path:`${out}/cursor-trail-fine.png`});
 await p.evaluate(()=>{fuzz.engine.setPaused(true);});
 const count=await p.locator('[data-cursor-trail]').getAttribute('data-particles');await p.mouse.move(50,50);
 assert.equal(await p.locator('[data-cursor-trail]').getAttribute('data-particles'),count);
 await p.evaluate(()=>{fuzz.engine.setPaused(false);fuzz.ui.setInit({gentle:true});go(50);});
 const clear=await p.locator('[data-cursor-trail]').evaluate(c=>{const a=c.getContext('2d').getImageData(0,0,c.width,c.height).data;return !a.some((v,i)=>i%4===3&&v);});assert(clear,'Gentle clears trail');
 assert.deepEqual(errors,[]);console.log('visuals passed');
}finally{await browser.close();}
