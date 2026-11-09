import assert from 'node:assert/strict';
import {createRequire} from 'node:module';
const {chromium}=createRequire('C:/wt-fe/e2e/package.json')('playwright');
const browser=await chromium.launch({channel:'chrome',headless:true,args:['--mute-audio']});
try {
 const p=await browser.newPage({viewport:{width:1100,height:850}}),errors=[];
 p.on('pageerror',e=>errors.push(e.message));
 await p.addInitScript(()=>{
  const draw=CanvasRenderingContext2D.prototype.drawImage;
  const clear=CanvasRenderingContext2D.prototype.clearRect;
  CanvasRenderingContext2D.prototype.clearRect=function(...args){this.shades=[];return clear.apply(this,args);};
  CanvasRenderingContext2D.prototype.drawImage=function(im,...args){
   const src=im.currentSrc||'',mat=this.getTransform();
   if(!this.canvas.isConnected&&this.canvas.width===328&&this.canvas.height===580){
    const match=/capsule-(red|purple|gold)\.webp$/.exec(src);
    if(match||(im instanceof HTMLCanvasElement&&args.length===4&&mat.e!==0&&mat.f!==0)){
      (this.shades??=[]).push([mat.e/2,mat.f/2,match?.[1]||'cyan']);
    }
    if(src.endsWith('/globe-mask.webp')){window.pileShapes=this.shades;this.shades=[];}
   }
   return draw.call(this,im,...args);
  };
 });
 const base=(process.env.FUZZ_URL||'http://localhost:8807/fuzz/index.html');
 await p.goto(base+'?fresh=1&buy=dispenser');
 await p.waitForFunction(()=>window.pileShapes?.length===58);
 const inspect=()=>p.evaluate(()=>window.pileShapes);
 const initial=await inspect();
 await p.evaluate(()=>{const e=fuzz.engine;window.step=e.step.bind(e);e.step=()=>{};});
 await p.locator('.prize-crank').click();await p.evaluate(()=>{for(let i=0;i<40;i++)step(100);});
 const finished=await inspect(),state=await p.evaluate(()=>fuzz.engine.run.store.dispenser.artPile);
 const frames=await p.evaluate(async()=>{const d=await (await fetch('assets/arcade/dispenser/motion.json')).json();return d.frames[0].map(x=>x.id);});
 const colorById=new Map(frames.map((id,i)=>[id,initial[i][2]]));
 assert.equal(state.length,58);
 for(const [i,ball] of state.entries()){
   if(colorById.has(ball.id))assert.equal(finished[i][2],colorById.get(ball.id));
   assert.equal(ball.shade,finished[i][2]);
 }
 await p.evaluate(async()=>{const {serialize,SAVE_KEY}=await import('./sim/save.js');const e=fuzz.engine;
  localStorage.setItem(SAVE_KEY,serialize(e.run,{...fuzz.life,contract:true},{again:true}));});
 await p.goto(base);await p.waitForFunction(()=>window.pileShapes?.length===58);
 const restored=await inspect();assert.deepEqual(restored,finished,'same visible coordinates and materials after full reload');
 await p.evaluate(()=>{const e=fuzz.engine;window.step=e.step.bind(e);e.step=()=>{};for(let i=0;i<320;i++)step(100);});
 await p.locator('.prize-crank').click();await p.evaluate(()=>{for(let i=0;i<40;i++)step(100);});
 const second=await inspect();assert.deepEqual(second.map(b=>b[2]),finished.map(b=>b[2]));
 assert.deepEqual(errors,[]);console.log('Verified actual drawn sprite colors on the first refill, a saved-game reload and another refill.');
}finally{await browser.close();}
