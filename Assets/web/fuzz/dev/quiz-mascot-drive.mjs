import {mkdirSync} from 'node:fs';
import {resolve} from 'node:path';
const out=resolve(process.argv[2]||'dev/shots/reactions');mkdirSync(out,{recursive:true});
import {createRequire} from 'node:module';
import assert from 'node:assert/strict';
const {chromium}=createRequire('C:/wt-fe/e2e/package.json')('playwright');
const browser=await chromium.launch({channel:'chrome',headless:true,args:['--mute-audio']});
try{
for(const width of [1100,390]){
 const p=await browser.newPage({viewport:{width,height:850}}),errors=[];p.on('pageerror',e=>errors.push(e.message));
 await p.goto(`${process.env.FUZZ_URL||'http://localhost:8807/fuzz/index.html'}?fresh=1&pictures=none&buy=quiz`);
 await p.waitForFunction(()=>window.fuzz?.engine.scopes.get('quiz'));
 await p.waitForTimeout(1000);
 const dock=p.locator('.xq-dock');
 assert.equal(await p.locator('.xe-f canvas').count(),1);
 assert.equal(await dock.evaluate(el=>Boolean(el.closest('.xe[data-emi]'))),true);
 for(const [i,right] of [false,true,true,true].entries()){
 await p.evaluate(right=>{const q=window.fuzz.engine.scopes.get('quiz').ret;q.close();const o=q.ask('none');for(const [a,b]of o.buttons)if((a.tone==='yes')===right){b.dataset.testAnswer='yes';break;}},right);
 await p.waitForTimeout(350);
 await p.locator('[data-test-answer=yes]').click();
 await p.waitForTimeout(150);
 const expected=i===3?'celebrate':right?'correct':'wrong';
 assert.equal(await p.locator('.xe[data-emi]').getAttribute('data-reaction'),expected);
 assert.equal(await dock.locator('.dk-b').evaluate(e=>getComputedStyle(e).overflow),'clip');
 assert.equal(await p.locator('.xe-sprite>img').evaluate(e=>e.complete&&e.naturalWidth>0),true);
 await dock.screenshot({path:`${out}/quiz-emi-${width}-${expected}.png`});
 await p.waitForTimeout(1700);
 assert.equal(await p.locator('.xq').count(),0);
 }
 await p.waitForTimeout(1200);
 assert.equal(await p.locator('.xe[data-emi]').getAttribute('data-reaction'),'idle');
 const badge=await p.locator('.xq-idle').boundingBox(), emi=await p.locator('.xe[data-emi]').boundingBox();
 const gap=Math.max(0,emi.x-badge.x-badge.width,badge.x-emi.x-emi.width,emi.y-badge.y-badge.height,badge.y-emi.y-emi.height);
 assert.ok(gap<=6,`countdown gap ${gap}px`);
 assert.match(await p.locator('.xq-idle').innerText(),/^Next question in: \d+s$/);
 const before=(await dock.boundingBox()).width;
 const bodyWidth=(await p.locator('.xe[data-emi]').boundingBox()).width;
 await dock.locator('.dk-resize').focus();await p.keyboard.press('ArrowLeft');
 assert.notEqual((await dock.boundingBox()).width,before,'the attached question resizes');
 await p.waitForFunction(width=>document.querySelector('.xe[data-emi]').offsetWidth!==width,bodyWidth);
 await p.evaluate(()=>{const o=window.fuzz.engine.scopes.get('quiz').ret.ask('none');for(const [a,b]of o.buttons)if(a.tone==='yes'){b.dataset.resizeAnswer='yes';break;}});
 await p.waitForTimeout(350);await p.locator('[data-resize-answer=yes]').click();
 assert.equal(await p.locator('.xe[data-emi]').getAttribute('data-reaction'),'correct');
 await p.evaluate(()=>window.fuzz.engine.setPaused(true));await p.waitForTimeout(50);
 const frozen=await p.locator('.xe-sprite').evaluate(e=>e.style.transform);
 await p.waitForTimeout(300);
 assert.equal(await p.locator('.xe-sprite').evaluate(e=>e.style.transform),frozen,'EMI obeys pause');
 await p.evaluate(()=>window.fuzz.engine.setPaused(false));
 await p.evaluate(()=>window.fuzz.ui.setInit({gentle:true}));
 await p.waitForTimeout(100);
 assert.equal(await p.locator('.xe-sprite').evaluate(e=>e.style.transform),'');
 await p.evaluate(()=>{const e=window.fuzz.engine;e.switches.set('quiz',false);e.switches.set('quiz',true);});
 assert.equal(await p.locator('.xe[data-emi]').count(),1,'toggle cleans up the old mascot');
 assert.deepEqual(errors,[]);console.log('quiz passed',width);await p.close();
}
}finally{await browser.close();}
