import {mkdirSync} from 'node:fs';
import {resolve} from 'node:path';
const out=resolve(process.argv[2]||'dev/shots/prizes');mkdirSync(out,{recursive:true});
import {createRequire} from 'node:module';
import assert from 'node:assert/strict';
const {chromium}=createRequire('C:/wt-fe/e2e/package.json')('playwright');
const b=await chromium.launch({channel:'chrome',headless:true,args:['--mute-audio']});
try{
const p=await b.newPage({viewport:{width:1440,height:900}}),errors=[];p.on('pageerror',e=>errors.push(e.message));
await p.goto('http://localhost:8807/fuzz/index.html?fresh=1&buy=a_hum');await p.waitForFunction(()=>window.fuzz);
await p.evaluate(()=>{const e=fuzz.engine;e.run.store.a_phones={on:true};e.grant('a_phones');window.advance=e.step.bind(e);e.step=()=>{};});
await p.waitForSelector('[data-hum-wave]');
for(const split of [false,true]){
if(split)await p.evaluate(()=>fuzz.engine.grant('a_bin'));
let lo=999,hi=0;
for(let i=0;i<100;i++){
 const bounds=await p.evaluate(()=>{advance(100);const c=document.querySelector('[data-hum-wave]'),g=c.getContext('2d'),a=g.getImageData(0,0,c.width,c.height).data;let lo=c.height,hi=-1;for(let y=0;y<c.height;y++)for(let x=0;x<c.width;x++)if(a[(y*c.width+x)*4+3]){lo=Math.min(lo,y);hi=Math.max(hi,y);}return{lo,hi,height:c.height};});
 assert(bounds.lo>2&&bounds.hi<bounds.height-3,'wave stays inside both vertical canvas edges');lo=Math.min(lo,bounds.lo);hi=Math.max(hi,bounds.hi);
}
assert(hi-lo>170,'headphone amplitude retained');console.log({split,lo,hi});
}
await p.screenshot({path:`${out}/wave-unclipped.png`});
assert.deepEqual(errors,[]);
}finally{await b.close();}
