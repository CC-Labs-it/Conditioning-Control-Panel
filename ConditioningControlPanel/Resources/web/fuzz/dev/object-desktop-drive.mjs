// Real mouse and native Chromium touch checks for the compact object desktop.
// Run against dev/serve.mjs. All storage is isolated from the owner's browser.
// OBJECT_BROWSER=firefox retains comparison support; its touch protocol cannot drive native scrolling.
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { mkdirSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { clearCards } from './pile-check.mjs';

const require = createRequire('C:/wt-fe/e2e/');
const pw = require('playwright-core/lib/coreBundle').inprocess.createInProcessPlaywright();
const base = process.env.FUZZ_URL || 'http://localhost:8798/fuzz/index.html';
const out = process.env.OBJECT_SHOTS || fileURLToPath(new URL('./shots/object-desktop/', import.meta.url));
mkdirSync(out, { recursive: true });
const checks = [], failures = [], pageErrors = [];
const chromium = process.env.OBJECT_BROWSER !== 'firefox';
const browser = chromium ? await pw.chromium.launch({headless:true,
  executablePath:process.env.CHROME_EXE||'C:/Program Files/Google/Chrome/Application/chrome.exe',args:['--mute-audio']}) : await pw.firefox.launch({ headless: true,
  executablePath: process.env.FF_EXE || 'C:/Users/Admin/AppData/Local/ms-playwright/firefox-1538/firefox/firefox.exe',
  firefoxUserPrefs: { 'media.volume_scale': '0.0' } });
const root = (page, id) => page.locator(`.dk[data-fz-owner="${id}"]`).first();
const chip = (page, id) => page.locator(`.dk-chip[data-fz-owner="${id}"]`).first();
const point = async locator => { const r = await locator.boundingBox(); assert.ok(r, 'control is visible'); return { x:r.x+r.width/2, y:r.y+r.height/2 }; };
const position = async locator => { const r = await locator.boundingBox(); assert.ok(r); return { x:Math.round(r.x), y:Math.round(r.y) }; };
const order = page => page.locator('.dk-chip').evaluateAll(els => [...els].sort((a,b) => (+getComputedStyle(a).order)-(+getComputedStyle(b).order) || a.getBoundingClientRect().x-b.getBoundingClientRect().x).map(e => e.dataset.fzOwner));
async function step(page, ms) { await page.evaluate(ms => { while(ms>0) {const dt=Math.min(ms,100);window.fuzz.engine.step(dt);ms-=dt;} },ms); await page.waitForTimeout(70); }
async function check(name, fn) { if(process.env.OBJECT_FILTER&&!new RegExp(process.env.OBJECT_FILTER).test(name))return;try { const result=await fn(); checks.push({name,...result}); console.log(`PASS ${name}`); } catch(e) { failures.push({name,error:e.stack}); console.log(`FAIL ${name}: ${e.message}`); } }
function pointer(page, touch, cdp = null) {
  const send=(type,p)=>cdp?cdp.send('Input.dispatchTouchEvent',{type,touchPoints:type==='touchEnd'?[]:[{x:p.x,y:p.y,radiusX:8,radiusY:8,rotationAngle:0,force:1,id:1}],modifiers:0}):pw._connection.toImpl(page).delegate._session.send('Page.dispatchTouchEvent', {type,touchPoints:p?[{x:p.x,y:p.y,radiusX:8,radiusY:8,rotationAngle:0,force:1}]:[],modifiers:0});
  let at;
  return {
    async down(p) {at=p; if(touch) await send('touchStart',p); else {await page.mouse.move(p.x,p.y);await page.mouse.down();}},
    async move(p) {at=p; if(touch) await send('touchMove',p); else await page.mouse.move(p.x,p.y,{steps:6});},
    async up() {if(touch) await send('touchEnd',at);else await page.mouse.up();},
    async drag(from,to,hold=430) {await this.down(from);await page.waitForTimeout(hold);for(let n=1;n<=8;n++){await this.move({x:from.x+(to.x-from.x)*n/8,y:from.y+(to.y-from.y)*n/8});await page.waitForTimeout(20);}await this.up();await page.waitForTimeout(90);},
  };
}
async function press(locator, touch) { await locator.scrollIntoViewIfNeeded(); if(touch) await locator.tap();else await locator.click(); }
async function visible(page,id,value,touch) {
  const current=await root(page,id).isVisible();
  if(current!==value) await press(chip(page,id),touch);
  assert.equal(await root(page,id).isVisible(),value,`${id} tab toggles visibility`);
}
async function only(page,id,touch) {
  const ids=await page.locator('.dk:not(.door)').evaluateAll(els=>els.map(e=>e.dataset.fzOwner).filter(Boolean));
  for(const other of [...new Set(ids)]) if(other!==id && await chip(page,other).count()) await visible(page,other,false,touch);
  await visible(page,id,true,touch);
  // Keyboard positioning is itself a public interaction, not a DOM layout mutation.
  await chip(page,id).focus();
  for(let n=0;n<50;n++) {await page.keyboard.press('Control+Shift+ArrowLeft');await page.keyboard.press('Control+Shift+ArrowUp');}
  await page.keyboard.press('Control+Shift+ArrowDown');
}
async function snapshot(page,name) {
  const stats=await page.evaluate(()=>({width:innerWidth,pageWidth:document.documentElement.scrollWidth,
    visible:document.querySelectorAll('.dk:not(.min):not(.door)').length,tabs:document.querySelectorAll('.dk-chip').length,
    wake:(()=>{const e=document.getElementById('wake'),r=e.getBoundingClientRect();return document.elementFromPoint(r.x+r.width/2,r.y+r.height/2)?.id;})()}));
  assert.ok(stats.pageWidth<=stats.width+1,JSON.stringify(stats));assert.equal(stats.wake,'wake');
  await page.screenshot({path:`${out}/${name}.png`}); return stats;
}
async function freshPreview(page) {
  // Leave the old preview first: its pagehide writes preferences one last time.
  await page.goto(`${base}?fresh=1`);await page.waitForFunction(()=>!!window.fuzz);
  await page.evaluate(()=>localStorage.removeItem('fuzz.preview.prefs.v1'));
  await page.goto(`${base}?preview=toys`);await page.waitForFunction(()=>window.fuzz?.engine.scopes.has('dispenser'));await page.waitForTimeout(3200);
}

try {
  for(const touch of [false,true]) {
    const label=touch?'phone':'desktop';
    const context=await browser.newContext({viewport:touch?{width:390,height:844}:{width:1366,height:900},hasTouch:touch});
    await context.addInitScript(()=>{
      if(!localStorage.getItem('object-test-seeded')) {
        localStorage.setItem('object-test-seeded','1');
        localStorage.setItem('fuzz.save.v2','normal save untouched');
        localStorage.setItem('fuzz.preview.prefs.v1',JSON.stringify({windowLayouts:{
          'dispenser:_dock1':{edge:'left',x:24,y:90,w:700,h:600,min:false,z:77},
          'mini_slots:_dock1':{edge:'right',x:40,y:100,w:480,h:500,min:true,z:20},
        }}));
      }
    });
    const page=await context.newPage();page.setDefaultTimeout(5000);
    page.on('pageerror',e=>pageErrors.push(`${label}: ${e.message}`));
    await page.goto(`${base}?preview=toys`);await page.waitForFunction(()=>window.fuzz?.engine.scopes.has('toy_box'));await page.waitForTimeout(2600);
    const input=pointer(page,touch,chromium&&touch?await context.newCDPSession(page):null);
    await check(`${label} migration and chrome`,async()=>{
      assert.equal(await page.locator('.dk .dk-t,.dk .dk-f,.dk .dk-resize').count(),0,'no window buttons or resize chrome');
      const box=await root(page,'dispenser').boundingBox();assert.ok(box.width<400 && box.height<400,JSON.stringify(box));
      assert.equal(await root(page,'mini_slots').isVisible(),false,'legacy hidden state retained');
      const style=await root(page,'dispenser').evaluate(e=>{const s=getComputedStyle(e);return {bg:s.backgroundColor,border:s.borderTopWidth};});
      assert.equal(style.bg,'rgba(0, 0, 0, 0)');assert.equal(style.border,'0px');
      return await snapshot(page,`${label}-initial`);
    });
    await check(`${label} object hold and ordinary click`,async()=>{
      await only(page,'dispenser',touch); const before=await position(root(page,'dispenser'));
      const p=await point(page.locator('.prize-globe'));await input.drag(p,{x:p.x+72,y:p.y+60});
      const after=await position(root(page,'dispenser'));assert.ok(after.x-before.x>50 && after.y-before.y>40,JSON.stringify({before,after}));
      assert.equal(await page.evaluate(()=>window.fuzz.state.store.dispenser.machine.phase),'ready','moving casing does not dispense');
      await press(page.locator('.prize-globe'),touch);assert.deepEqual(await position(root(page,'dispenser')),after,'short press does not move');
      return {before,after};
    });
    if (!touch) await check('desktop immediate mouse drag on overlapping toys',async()=>{
      await only(page,'dispenser',false);
      await visible(page,'toy_box',true,false);
      const toy=chip(page,'toy_box'); await toy.focus();
      for(let n=0;n<50;n++) {await page.keyboard.press('Control+Shift+ArrowLeft');await page.keyboard.press('Control+Shift+ArrowUp');}
      await page.keyboard.press('Control+Shift+ArrowDown');
      // Reproduce the overlap: pull the upper toy away without waiting for a hold.
      const boxBefore=await position(root(page,'toy_box'));
      const machineBefore=await position(root(page,'dispenser'));
      const lid=page.locator('[data-arcade=toy_box] .tb-clock');
      const p=await point(lid);
      await input.drag(p,{x:p.x+260,y:p.y+110},0);
      const boxAfter=await position(root(page,'toy_box'));
      assert.ok(boxAfter.x-boxBefore.x>240 && boxAfter.y-boxBefore.y>90,JSON.stringify({boxBefore,boxAfter}));
      assert.deepEqual(await position(root(page,'dispenser')),machineBefore,'only the upper toy moves');
      const globe=await point(page.locator('.prize-globe'));
      await input.drag(globe,{x:globe.x+110,y:globe.y+80},0);
      const machineAfter=await position(root(page,'dispenser'));
      assert.ok(machineAfter.x-machineBefore.x>90 && machineAfter.y-machineBefore.y>60);
      assert.equal(await page.evaluate(()=>window.fuzz.state.store.dispenser.machine.phase),'ready','drag never turns the crank');
      await page.reload(); await page.waitForFunction(()=>window.fuzz?.engine.scopes.has('toy_box'));await page.waitForTimeout(500);
      assert.deepEqual(await position(root(page,'toy_box')),boxAfter,'toy position survives reload');
      assert.deepEqual(await position(root(page,'dispenser')),machineAfter,'dispenser position survives reload');
      return {boxBefore,boxAfter,machineBefore,machineAfter};
    });
    await check(`${label} action and hidden readiness`,async()=>{
      await only(page,'dispenser',touch); const before=await position(root(page,'dispenser'));
      assert.equal(await chip(page,'dispenser').getAttribute('data-ready'),'true');
      await press(page.getByRole('button',{name:'Turn crank',exact:true}),touch);
      assert.deepEqual(await position(root(page,'dispenser')),before,'crank must not move machine');
      assert.equal(await chip(page,'dispenser').getAttribute('data-ready'),'false');
      await visible(page,'dispenser',false,touch);await step(page,18000);
      assert.equal(await root(page,'dispenser').isVisible(),false);
      assert.equal(await page.evaluate(()=>window.fuzz.state.store.dispenser.machine.phase),'ready');
      assert.equal(await chip(page,'dispenser').getAttribute('data-ready'),'true','hidden timer updates badge');
      assert.equal(await chip(page,'dispenser').locator('.dk-ready').isVisible(),true);
    });
    await check(`${label} slot action does not drag`,async()=>{
      await only(page,'mini_slots',touch);const before=await position(root(page,'mini_slots'));
      assert.equal(await chip(page,'mini_slots').getAttribute('data-ready'),'true');
      await press(page.getByRole('button',{name:'Spin',exact:true}),touch);
      assert.equal(await page.evaluate(()=>window.fuzz.state.store.mini_slots.machine.coins),2);
      assert.deepEqual(await position(root(page,'mini_slots')),before);
      assert.equal(await chip(page,'mini_slots').getAttribute('data-ready'),'false');
      await visible(page,'mini_slots',false,touch);await step(page,4000);
      assert.equal(await chip(page,'mini_slots').getAttribute('data-ready'),'true');
    });
    await check(`${label} metronome lever is an action`,async()=>{
      await only(page,'a_tick',touch);
      await page.evaluate(()=>window.fuzz.ui.setInit({gentle:true}));await page.waitForTimeout(90);
      const before=await position(root(page,'a_tick'));
      const initial=await page.evaluate(()=>({taps:window.fuzz.state.taps,tick:window.fuzz.state.sources.tick||0}));
      const lever=page.locator('.mt-lever');
      for(let n=0;n<16;n++){await press(lever,touch);await page.waitForTimeout(90);}
      const after=await page.evaluate(()=>({taps:window.fuzz.state.taps,tick:window.fuzz.state.sources.tick||0}));
      assert.equal(after.taps,initial.taps,'lever does not also press main button');assert.ok(after.tick>initial.tick,'lever pays rhythm reward');
      assert.deepEqual(await position(root(page,'a_tick')),before,'lever presses do not move metronome');
      assert.notEqual(await chip(page,'a_tick').getAttribute('data-ready'),'true','continuous rhythm has no permanent red dot');
      await page.evaluate(()=>window.fuzz.ui.setInit({gentle:false}));return {initial,after};
    });
    await check(`${label} metronome tempo weight`,async()=>{
      await only(page,'a_tick',touch);
      const weight=page.locator('.mt-weight'), arm=page.locator('.mt-arm'), casing=root(page,'a_tick');
      const before=await position(casing);
      await page.evaluate(()=>{
        const f=window.fuzz;window.metronomeTicks=[];
        const original=f.engine.audio.play.bind(f.engine.audio);
        f.engine.audio.play=(path,opts)=>{if(path.endsWith('count_tick.mp3'))window.metronomeTicks.push(f.state.clock);return original(path,opts);};
      });
      // Grab a moving weight at its current visible location without waiting for a hold.
      const p=await point(weight);await input.down(p);
      assert.equal(await page.locator('.tc-metronome').getAttribute('data-adjusting'),'true');
      const frozen=await arm.getAttribute('transform');
      await page.evaluate(()=>{window.metronomeTicks=[];});
      await step(page,1800);
      assert.equal(await arm.getAttribute('transform'),frozen,'pendulum remains frozen while held');
      assert.equal(await page.evaluate(()=>window.metronomeTicks.length),0,'no ticks during adjustment');
      await input.move({x:p.x,y:p.y+95});
      assert.equal(await weight.getAttribute('aria-valuenow'),'208');
      assert.equal(await page.locator('.mt-bpm').textContent(),'208 BPM','display updates before release');
      assert.equal(await arm.getAttribute('transform'),frozen);
      await input.up();
      assert.deepEqual(await position(casing),before,'weight drag never moves the object');
      await page.evaluate(()=>{window.metronomeTicks=[];for(let i=0;i<200;i++)window.fuzz.engine.step(10);});
      const fast=await page.evaluate(()=>window.metronomeTicks.slice());
      assert.ok(fast.length>=6 && fast.length<=8,JSON.stringify(fast));
      for(let i=1;i<fast.length;i++)assert.ok(Math.abs(fast[i]-fast[i-1]-60000/208)<25,'fast sound cadence matches BPM');
      const q=await point(weight);await input.down(q);await input.move({x:q.x,y:q.y-120});
      assert.equal(await weight.getAttribute('aria-valuenow'),'40');await input.up();
      await page.evaluate(()=>{window.metronomeTicks=[];for(let i=0;i<460;i++)window.fuzz.engine.step(10);});
      const slow=await page.evaluate(()=>window.metronomeTicks.slice());
      assert.ok(slow.length>=3 && slow.length<=4,JSON.stringify(slow));
      for(let i=1;i<slow.length;i++)assert.ok(Math.abs(slow[i]-slow[i-1]-1500)<25,'slow sound cadence matches BPM');
      await weight.focus();await page.keyboard.press('ArrowDown');assert.equal(await weight.getAttribute('aria-valuenow'),'41');
      await page.keyboard.press('End');assert.equal(await weight.getAttribute('aria-valuenow'),'208');
      await page.keyboard.press('Home');assert.equal(await weight.getAttribute('aria-valuenow'),'40');
      await page.evaluate(()=>{window.fuzz.engine.switches.set('a_tick',false);window.fuzz.engine.switches.set('a_tick',true);});
      assert.equal(await weight.getAttribute('aria-valuenow'),'40','tempo restores from saved feature state');
      await page.evaluate(()=>window.fuzz.ui.setInit({gentle:true}));await page.waitForTimeout(70);
      assert.equal(await arm.getAttribute('transform'),'rotate(0 120 174)','gentle keeps the rod still');
      for (const interruption of ['pause','suspend','blur']) {
        await input.down(await point(weight));
        assert.equal(await page.locator('.tc-metronome').getAttribute('data-adjusting'),'true');
        await page.evaluate(kind=>{
          const f=window.fuzz;
          if(kind==='pause')f.engine.setPaused(true);
          else if(kind==='suspend')f.engine.setSuspended(true,'test');
          else window.dispatchEvent(new Event('blur'));
        },interruption);
        assert.equal(await page.locator('.tc-metronome').getAttribute('data-adjusting'),'false',`${interruption} releases the weight`);
        await input.up();
        await page.evaluate(()=>{window.fuzz.engine.setPaused(false);window.fuzz.engine.setSuspended(false,'test');});
      }
      const casePoint=await page.locator('.tc-metronome svg').evaluate(e=>{const p=new DOMPoint(50,185).matrixTransform(e.getScreenCTM());return{x:p.x,y:p.y};});
      const caseBefore=await position(casing);
      await input.drag(casePoint,{x:casePoint.x+60,y:casePoint.y+35},touch?430:0);
      const caseAfter=await position(casing);
      assert.ok(caseAfter.x-caseBefore.x>45 && caseAfter.y-caseBefore.y>25,'case remains movable');
      await page.screenshot({path:`${out}/${label}-metronome-tempo.png`});
      await page.evaluate(()=>window.fuzz.ui.setInit({gentle:false}));
      return {fastTicks:fast.length,slowTicks:slow.length};
    });
    await check(`${label} toy piece owns its drag`,async()=>{
      await only(page,'toy_box',touch);
      if(await page.evaluate(()=>window.fuzz.state.store.toy_box.box.phase!=='play')) await step(page,await page.evaluate(()=>window.fuzz.state.store.toy_box.box.nextIn*1000+80));
      const pieces=await page.evaluate(()=>[...window.fuzz.state.store.toy_box.box.pieces]);
      const before=await position(root(page,'toy_box'));
      const first=pieces[0],from=await point(page.locator(`[data-toy-piece="${first}"]`)),to=await point(page.locator(`[data-toy-slot="${first}"]`));
      await input.drag(from,to,430);
      assert.deepEqual(await position(root(page,'toy_box')),before,'piece hold/drag must not move box');
      for(const piece of pieces.slice(1)) {await press(page.locator(`[data-toy-piece="${piece}"]`),touch);await press(page.locator(`[data-toy-slot="${piece}"]`),touch);}
      assert.ok(await page.evaluate(()=>window.fuzz.state.store.toy_box.box.completed>=1),'actual piece gestures completed box');
    });
    await check(`${label} pause cancels an object drag`,async()=>{
      await only(page,'dispenser',touch);const p=await point(page.locator('.prize-globe'));
      await input.down(p);await page.waitForTimeout(430);await input.move({x:p.x+20,y:p.y+20});
      await page.keyboard.press('Escape');const atPause=await position(root(page,'dispenser'));
      await input.move({x:p.x+90,y:p.y+90});await input.up();
      assert.deepEqual(await position(root(page,'dispenser')),atPause);assert.equal(await root(page,'dispenser').evaluate(e=>e.classList.contains('dragging')),false);
      await page.keyboard.press('Escape');
    });
    await check(`${label} tab reorder and persistent layout`,async()=>{
      const ids=await order(page),a=chip(page,ids[0]),b=chip(page,ids[1]);
      await a.scrollIntoViewIfNeeded();const from=await point(a),to=await point(b);
      const visibility=await root(page,ids[0]).isVisible();
      await input.drag(from,{x:to.x+Math.min(8,15),y:to.y},touch?430:0);
      const sorted=await order(page);assert.notDeepEqual(sorted,ids,'tab drag changes order');
      assert.equal(await root(page,ids[0]).isVisible(),visibility,'tab drag never toggles visibility');
      assert.deepEqual(await page.locator('.dk-chip').evaluateAll(es=>es.map(e=>e.dataset.fzOwner)),sorted,'DOM and visual tab order agree');
      await chip(page,ids[0]).focus();await page.keyboard.press('Tab');
      assert.equal(await page.evaluate(()=>document.activeElement?.dataset.fzOwner),sorted[sorted.indexOf(ids[0])+1],'native keyboard Tab follows reordered strip');
      const layout=await page.evaluate(()=>structuredClone(window.fuzz.state.store.dispenser._dock1));
      await page.reload();await page.waitForFunction(()=>window.fuzz?.engine.scopes.has('dispenser'));await page.waitForTimeout(600);
      assert.deepEqual(await order(page),sorted,'tab order survives reload');
      const restored=await page.evaluate(()=>window.fuzz.state.store.dispenser._dock1);
      for(const key of ['x','y','min']) assert.equal(restored[key],layout[key],`${key} persists`);
      await page.evaluate(()=>window.fuzz.newRun());await page.waitForLoadState('load');await page.waitForFunction(()=>window.fuzz?.engine.scopes.has('dispenser'));await page.waitForTimeout(600);
      assert.deepEqual(await order(page),sorted,'tab order survives new run');
      assert.equal(await page.evaluate(()=>localStorage.getItem('fuzz.save.v2')),'normal save untouched');
      return {order:sorted};
    });
    await check(`${label} picture frame uses shared tabs and hold`,async()=>{
      await only(page,'v_frame',touch);const frame=root(page,'v_frame'),before=await position(frame);
      assert.equal(await frame.locator('.scr').count(),1);assert.equal(await frame.locator('.scr-grip').count(),0);
      const p=await point(frame.locator('.scr-body'));await input.drag(p,{x:p.x+52,y:p.y+40});
      const after=await position(frame);assert.ok(after.x-before.x>35&&after.y-before.y>25,JSON.stringify({before,after}));
      await visible(page,'v_frame',false,touch);assert.equal(await page.locator('.scr[data-fz-owner="v_frame"]').isVisible(),false);
      await visible(page,'v_frame',true,touch);assert.deepEqual(await position(frame),after);
      return await snapshot(page,`${label}-picture-frame`);
    });
    await check(`${label} hidden race releases playback`,async()=>{
      await only(page,'mini_race',touch);const video=root(page,'mini_race').locator('video');
      await page.waitForFunction(()=>document.querySelector('[data-arcade="mini_race"] video')?.readyState>=2);
      await visible(page,'mini_race',false,touch);
      assert.equal(await video.evaluate(e=>e.paused),true,'tab hide immediately pauses video');
      assert.equal(await chip(page,'mini_race').getAttribute('data-ready'),'true');
    });
    await check(`${label} mixer disclosure and controls`,async()=>{
      const toggle=page.locator('#audio-toggle'),mixer=page.locator('#audio-mixer');
      await press(toggle,touch);
      const before=await position(mixer);
      const slider=mixer.locator('.mix-row input').first(),range=await slider.boundingBox(),old=await slider.inputValue();
      await input.drag({x:range.x+range.width*.8,y:range.y+range.height/2},{x:range.x+range.width*.2,y:range.y+range.height/2},0);
      assert.notEqual(await slider.inputValue(),old,'real slider drag adjusts volume');assert.deepEqual(await position(mixer),before);
      if(touch&&!chromium){await press(toggle,touch);return {nativeScroll:'Checked with Chromium'};}
      const scroller=mixer,size=await scroller.boundingBox();
      assert.ok(await scroller.evaluate(e=>e.scrollHeight>e.clientHeight),'preview mixer has scrolling content');
      const scrollStart=await page.evaluate(p=>{const e=document.elementFromPoint(p.x,p.y);return {tag:e?.tagName,cls:e?.className,ancestors:(()=>{const a=[];for(let n=e;n&&a.length<5;n=n.parentElement)a.push({tag:n.tagName,cls:n.className,touch:getComputedStyle(n).touchAction});return a;})()};},{x:size.x+size.width-10,y:size.y+size.height-40});
      if(touch)await input.drag({x:size.x+size.width-10,y:size.y+size.height-40},{x:size.x+size.width-10,y:size.y+35},0);
      else{await page.mouse.move(size.x+size.width-10,size.y+size.height/2);await page.mouse.wheel(0,500);await page.waitForTimeout(220);}
      await snapshot(page,`${label}-mixer-scroll`);
      assert.ok(await scroller.evaluate(e=>e.scrollTop)>0,`remaining sources scroll into view: ${JSON.stringify(scrollStart)}`);assert.deepEqual(await position(mixer),before,'scroll never moves the whole mixer');
      const result=await snapshot(page,`${label}-mixer`);await press(toggle,touch);return result;
    });
    await check(`${label} crowded real route and unlimited tabs`,async()=>{
      await page.goto(`${base}?fresh=1`);await page.waitForFunction(()=>!!window.fuzz);
      await page.evaluate(()=>{const f=window.fuzz;['eyes','sweet','slides','sell','trance'].forEach((lane,i)=>{f.state.picks[`f${i+1}`]=lane;});for(const item of f.items)if(item.group==='run'&&item.kind!=='end')f.grant(item.id);f.add(1e9);});
      await page.waitForTimeout(1400);const cardErrors=[];await clearCards(page,cardErrors,'object desktop');assert.deepEqual(cardErrors,[]);
      const tabs=page.locator('.dk-chip');
      for(let n=0;n<await tabs.count();n++){const tab=tabs.nth(n);const isDoor=await tab.evaluate(e=>[...document.querySelectorAll('.dk.door')].some(d=>d.dataset.fzOwner===e.dataset.fzOwner));if(!isDoor && await tab.getAttribute('aria-expanded')==='false')await press(tab,touch);}
      const stats=await snapshot(page,`${label}-crowded`);assert.ok(stats.tabs>12,'real route creates many tabs');assert.ok(stats.visible>12,'no automatic 12-object limit');
      const strip=await tabs.first().evaluate(e=>{const s=e.parentElement;return {scroll:s.scrollWidth,client:s.clientWidth,height:s.getBoundingClientRect().height};});
      assert.ok(strip.height<80,'tabs stay a compact single strip');
      const rect=await tabs.first().evaluate(e=>{const r=e.parentElement.getBoundingClientRect();return {left:r.left,right:r.right,top:r.top,bottom:r.bottom,w:innerWidth,h:innerHeight};});
      assert.ok(rect.left>=0&&rect.right<=rect.w&&rect.bottom<=rect.h&&rect.top>=rect.h-80,'whole-page effects cannot move the tab strip outside the viewport');
      return {...stats,strip};
    });
    await check(`${label} secondary theme surfaces`,async()=>{
      await page.goto(`${base}?buy=si_rules,si_pod&pictures=none`);await page.waitForFunction(()=>window.fuzz?.engine.scopes.has('si_rules'));await page.waitForTimeout(2400);
      await only(page,'si_rules',touch);assert.equal(await chip(page,'si_rules').getAttribute('data-ready'),'true');
      const rules=root(page,'si_rules').locator('button');assert.equal(await rules.count(),4);
      for(let n=0;n<4;n++){
        assert.equal(await rules.evaluateAll(es=>es.filter(e=>e.textContent.includes('✓')).length),n,'one rule completes per press');
        assert.equal(await rules.nth(n).isEnabled(),true,'current rule is actionable');
        await press(rules.nth(n),touch);
        if(touch&&n<3){assert.equal(await chip(page,'si_rules').getAttribute('data-ready'),'false');assert.equal(await rules.nth(n+1).isEnabled(),false);await step(page,8300);assert.equal(await chip(page,'si_rules').getAttribute('data-ready'),'true');}
      }
      assert.equal(await chip(page,'si_rules').getAttribute('data-ready'),'false');
      await visible(page,'si_rules',false,touch);await step(page,42000);assert.equal(await chip(page,'si_rules').getAttribute('data-ready'),'true');
      await only(page,'si_pod',touch);const pod=await root(page,'si_pod').boundingBox();assert.ok(pod.width<=310&&pod.height<260,JSON.stringify(pod));
      return await snapshot(page,`${label}-podcast`);
    });
    await check(`${label} time wheel uses a real tab`,async()=>{
      await page.goto(`${base}?buy=lo_wheel&pictures=none`);await page.waitForFunction(()=>window.fuzz?.engine.scopes.has('lo_wheel'));await page.waitForTimeout(2200);
      await only(page,'lo_wheel',touch);assert.equal(await chip(page,'lo_wheel').getAttribute('data-ready'),'true');
      const before=await position(root(page,'lo_wheel'));await press(root(page,'lo_wheel').locator('button').first(),touch);
      assert.equal(await chip(page,'lo_wheel').getAttribute('data-ready'),'false');assert.deepEqual(await position(root(page,'lo_wheel')),before);
      await visible(page,'lo_wheel',false,touch);await step(page,16000);assert.equal(await chip(page,'lo_wheel').getAttribute('data-ready'),'true');
      await visible(page,'lo_wheel',true,touch);return await snapshot(page,`${label}-time-wheel`);
    });
    await check(`${label} prize wheel and inbox readiness`,async()=>{
      await page.goto(`${base}?buy=wheel,inbox&pictures=none`);await page.waitForFunction(()=>window.fuzz?.engine.scopes.has('inbox'));await page.waitForTimeout(1800);
      await only(page,'wheel',touch);assert.equal(await chip(page,'wheel').getAttribute('data-ready'),'true');
      await press(root(page,'wheel').locator('button').first(),touch);assert.equal(await chip(page,'wheel').getAttribute('data-ready'),'false');
      await visible(page,'wheel',false,touch);await step(page,91000);assert.equal(await chip(page,'wheel').getAttribute('data-ready'),'true');
      await step(page,11000);assert.equal(await chip(page,'inbox').getAttribute('data-ready'),'true','new hidden message badges tab');
      await only(page,'inbox',touch);
      const notices=root(page,'inbox').locator('button');assert.ok(await notices.count()>0);
      for(let n=await notices.count()-1;n>=0;n--)await press(notices.nth(n),touch);
      assert.equal(await chip(page,'inbox').getAttribute('data-ready'),'false');
      return await snapshot(page,`${label}-inbox`);
    });
    await check(`${label} swipe feed gesture and tab hide`,async()=>{
      await page.goto(`${base}?buy=d_scroll&pictures=online`);await page.waitForFunction(()=>window.fuzz?.engine.scopes.has('d_scroll'));await page.waitForTimeout(1800);
      await only(page,'d_scroll',touch);const dock=root(page,'d_scroll'),before=await position(dock);
      const area=dock.locator('.scr-body > div').first(),p=await point(area);
      const taps=await page.evaluate(()=>window.fuzz.state.taps);await input.drag({x:p.x,y:p.y+35},{x:p.x,y:p.y-35},430);
      assert.deepEqual(await position(dock),before,'held feed swipe does not become object movement');assert.ok(await page.evaluate(()=>window.fuzz.state.taps)>taps,'swipe retains its gameplay action');
      if(touch){await press(area,touch);assert.equal(await page.locator('.routes-full').count(),1,'phone feed opens fullscreen');}
      await visible(page,'d_scroll',false,touch);assert.equal(await page.locator('.routes-full').count(),0);assert.equal(await area.isVisible(),false,'tab hides fullscreen feed too');
      await visible(page,'d_scroll',true,touch);return await snapshot(page,`${label}-swipe-feed`);
    });
    await check(`${label} four-piece box fits its reservation`,async()=>{
      await freshPreview(page);
      await page.evaluate(async()=>{
        const f=window.fuzz,{createToyBox}=await import('/fuzz/ui/unlocks/arcade/toy-box-model.js');
        f.engine.switches.set('toy_box',false);
        f.state.store.toy_box.box={...createToyBox(()=>.5),completed:2,round:2,nextIn:.1};
        f.engine.switches.set('toy_box',true);
      });
      if(touch)await only(page,'toy_box',touch);
      await step(page,250);await page.waitForFunction(()=>document.querySelectorAll('[data-toy-piece]').length===4);
      await page.waitForFunction(()=>[...document.querySelectorAll('[data-arcade="toy_box"] img')].every(e=>e.complete&&e.naturalWidth));
      const bounds=await root(page,'toy_box').boundingBox();assert.ok(bounds.height<=320,`four-piece height ${bounds.height}`);
      const reachable=await page.locator('[data-toy-piece],[data-toy-slot]').evaluateAll(es=>es.map(e=>{const r=e.getBoundingClientRect();return e.contains(document.elementFromPoint(r.x+r.width/2,r.y+r.height/2));}));
      assert.ok(reachable.every(Boolean),'every four-piece control is reachable');
      if(!touch){const collisions=await root(page,'toy_box').evaluate(e=>{const r=e.getBoundingClientRect();return [...document.querySelectorAll('.dk:not(.min):not(.door)')].filter(o=>o!==e).filter(o=>{const b=o.getBoundingClientRect();return r.left<b.right&&r.right>b.left&&r.top<b.bottom&&r.bottom>b.top;}).map(o=>o.dataset.fzOwner);});assert.deepEqual(collisions,[],'opening maximum box must not cover default neighbors');}
      await snapshot(page,`${label}-four-piece`);return {height:bounds.height,width:bounds.width};
    });
    await check(`${label} fresh default composition`,async()=>{
      await freshPreview(page);
      const taps=await page.evaluate(()=>window.fuzz.state.taps);await press(page.locator('#btn'),touch);
      assert.ok(await page.evaluate(()=>window.fuzz.state.taps)>taps,'fresh layout keeps main button usable');
      assert.equal(await page.locator('#audio-mixer').isVisible(),false,'new mixer starts tucked');
      const initial=await snapshot(page,`${label}-fresh`);
      await visible(page,'quiz',true,touch);
      const answers=await page.locator('.dk[data-fz-owner="quiz"] .xq-a').evaluateAll(es=>es.filter(e=>e.getClientRects().length).map(e=>{const r=e.getBoundingClientRect();return {label:e.textContent,reachable:e.contains(document.elementFromPoint(r.x+r.width/2,r.y+r.height/2))};}));
      assert.ok(answers.length>0,'opening the quiz tab exposes its answer controls');
      assert.ok(answers.every(a=>a.reachable),`fresh quiz answers remain reachable: ${JSON.stringify(answers)}`);
      await snapshot(page,`${label}-fresh-quiz`);return initial;
    });
    await context.close();
  }
} finally {
  await browser.close();writeFileSync(`${out}/checks.json`,JSON.stringify({checks,failures,pageErrors},null,2));
}
console.log(JSON.stringify({checks:checks.length,failures,pageErrors},null,2));
assert.deepEqual(pageErrors,[]);assert.deepEqual(failures,[]);
