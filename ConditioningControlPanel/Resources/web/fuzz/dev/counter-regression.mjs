// Regression for rolling digits disappearing when a second theme clips the counter text.
import assert from 'node:assert/strict';
import {createRequire} from 'node:module';
const {chromium}=createRequire('C:/wt-fe/e2e/package.json')(process.env.PW_DIR||'playwright');
const browser=await chromium.launch({channel:'chrome',headless:true,args:['--mute-audio']});
try {
 const page=await browser.newPage({viewport:{width:1100,height:850}}),errors=[];
 page.on('pageerror',e=>errors.push(e.message));
 await page.goto((process.env.FUZZ_URL||'http://localhost:8807/fuzz/index.html')+'?preview=toys&pictures=none');
 await page.waitForFunction(()=>window.fuzz);
 const purchases=await page.evaluate(()=>{
  const e=window.fuzz.engine;e.grant('counter');
  e.run.store.taste2={theme:'bimbo'};e.emit('taste2');
  let count=0;
  for(let i=0;i<25;i++) {const item=e.shop().rows.find(x=>!['fork','end'].includes(x.kind)&&e.price(x.id)<=e.run.fuzz);if(!item)break;if(e.buy(item.id))count++;}
  window.counterStep=e.step;e.step=()=>{};
  return count;
 });
 assert(purchases>5,'burst of real purchases');
 assert(await page.locator('body').evaluate(e=>e.classList.contains('mixed')));
 // A uniform backing isolates painted glyphs from decorative layers in the screenshot.
 await page.addStyleTag({content:'#fz-roll{background:#000!important}'});
 for(const width of [1100,390]) {
  await page.setViewportSize({width,height:850});
  for(const value of [98765,10000,9999,1000,999,0]) {
   await page.evaluate(value=>{const e=window.fuzz.engine;e.run.fuzz=value;window.counterStep(75);window.fuzz.ui.draw(true);},value);
   await page.waitForTimeout(550);
   const roll=page.locator('#fz-roll'),png=await roll.screenshot();
   const painted=await roll.evaluate(async(el,base64)=>{
    const image=new Image();image.src='data:image/png;base64,'+base64;await image.decode();
    const c=document.createElement('canvas');c.width=image.width;c.height=image.height;const g=c.getContext('2d');g.drawImage(image,0,0);
    const pixels=g.getImageData(0,0,c.width,c.height).data,rect=el.getBoundingClientRect();
    return [...el.querySelectorAll(':scope > .d')].map(d=>{const b=d.getBoundingClientRect();let ink=0;
     for(let x=Math.max(0,Math.ceil(b.left-rect.left));x<Math.min(c.width,Math.floor(b.right-rect.left));x++)for(let y=0;y<c.height;y++){const i=(y*c.width+x)*4;if(pixels[i]+pixels[i+1]+pixels[i+2]>150)ink++;}
     return ink;
    });
   },png.toString('base64'));
   const numeral=await roll.evaluate(el=>({
     text:el.textContent, solid:[...el.children].every(d=>getComputedStyle(d).color!=='rgba(0, 0, 0, 0)' && !d.childElementCount)
   }));
   assert.match(numeral.text,/^\d{1,3}(,\d{3})*$/);
   assert(numeral.solid,'a solid glyph per place, including the thousands separators');
   assert(painted.length>0&&painted.every(n=>n>12),`${width}px balance ${value}: every numeric column paints visible glyphs (${painted})`);
  }
 }
 await page.evaluate(()=>{const e=window.fuzz.engine;window.fuzz.ui.setInit({gentle:true});e.switches.set('counter',false);e.switches.set('counter',true);});
 assert(await page.locator('#count').isVisible(),'Gentle fallback remains visible after toggle');
 assert.deepEqual(errors,[]);console.log(`Passed: ${purchases} rapid purchases, mixed-theme glyph pixels, both viewport sizes, number-length changes, zero and Gentle toggle.`);
} finally {await browser.close();}
