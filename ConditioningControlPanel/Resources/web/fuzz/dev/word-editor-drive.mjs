import {mkdirSync} from 'node:fs';
import {resolve} from 'node:path';
const out=resolve(process.argv[2]||'dev/shots/word-editor');mkdirSync(out,{recursive:true});
import assert from 'node:assert/strict';
import {createRequire} from 'node:module';
const {chromium}=createRequire('C:/wt-fe/e2e/package.json')('playwright');
const browser=await chromium.launch({channel:'chrome',headless:true,args:['--mute-audio']});
try{
 const page=await browser.newPage({viewport:{width:1100,height:850}}),errors=[];
 page.on('pageerror',err=>errors.push(err.message));
 await page.goto((process.env.FUZZ_URL||'http://localhost:8807/fuzz/index.html')+'?preview=toys&shop=trails&equip=comet&wallet=unlimited&menu=words');
 const menu=page.getByRole('dialog').filter({has:page.locator('.word-editor')});
 await menu.waitFor();
 const choice=(k,id)=>menu.locator('[data-word-choice="'+k+'-'+id+'"]');
 await choice('palette','lagoon').click();await choice('style','electric').click();await choice('size','large').click();
 assert.equal(await page.evaluate(()=>fuzz.state.store.cursorKit.wordLook),undefined);
 await page.waitForTimeout(360);
 await menu.screenshot({path:out+'/word-editor-menu.png'});
 await menu.getByRole('button',{name:/Unlock editor/}).click();
 assert.equal(await page.evaluate(()=>fuzz.state.spent.word_editor),12000);
 assert.equal(await page.evaluate(()=>fuzz.state.store.cursorKit.words),true);
 for(const s of ['bold','outline','flicker','bounce','radiate','electric'])await choice('style',s).click();
 for(const c of ['theme','candy','solar','orchid','frost','lagoon'])await choice('palette',c).click();
 for(const z of ['small','medium','large','jumbo'])await choice('size',z).click();
 await menu.getByRole('button',{name:'Done'}).click();
 await page.evaluate(()=>{
  const e=fuzz.engine;window.tick=e.step.bind(e);e.step=()=>{};
  const g=document.querySelector('[data-cursor-trail]').getContext('2d'),txt=g.fillText.bind(g),clear=g.clearRect.bind(g);
  window.labels=[];g.fillText=(s,x,y)=>{labels.push({s,c:g.fillStyle,font:g.font});txt(s,x,y);};g.clearRect=(...a)=>{labels=[];clear(...a);};
 });
 for(let i=0;i<50;i++){await page.mouse.move(210+i*9,230+i*3);await page.evaluate(()=>tick(16));}
 const labels=await page.evaluate(()=>window.labels);assert(labels.length>=2);assert(labels.every(l=>['#60e8e0','#89bfff'].includes(l.c)));
 const mediumPixels = {RELAX:15, SINK:12, DEEPER:14, 'LET GO':13, DROP:16, BLANK:12};
 assert(labels.every(label=>Math.abs(parseFloat(label.font.match(/([\d.]+)px/)[1])-mediumPixels[label.s]*1.55)<.01),'equipped jumbo scale on each live word');
 const saved=await page.evaluate(async()=>{const {parse,serialize}=await import('./sim/save.js');return parse(serialize(fuzz.engine.run,fuzz.life,{})).run.store.cursorKit;});
 assert.deepEqual(saved.wordLook,{palette:'lagoon',style:'electric',size:'jumbo'});assert(saved.wordEditor);
 await page.setViewportSize({width:390,height:810});await page.getByRole('button',{name:'Cursor Shop',exact:true}).click();await menu.waitFor();
 assert.equal(await menu.locator('[aria-pressed=true][data-word-choice]').count(),3);
 assert(await menu.evaluate(m=>m.scrollWidth<=m.clientWidth+1));
 await menu.getByRole('checkbox',{name:'Following words'}).uncheck();assert.equal(await page.evaluate(()=>fuzz.state.store.cursorKit.words),false);
 assert.deepEqual(errors,[]);console.log('Passed editor purchase, swatch before unlock, 6 palettes, 6 styles, 4 sizes, saved choices, live rendering and phone layout.');
}finally{await browser.close();}
