// Play the complete Bimbodoll sequence. Optional FUZZ_URL selects the live Scrolller preview.
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
const require = createRequire(process.env.PW_ROOT || 'C:/wt-fe/e2e/package.json');
const { chromium } = require('playwright');
const url = process.env.FUZZ_URL || 'http://127.0.0.1:8829/fuzz/dev/play-ending.html?end=bi_end&reel=demo';
const live = new URL(url).searchParams.get('reel') === 'scrolller';
const browser = await chromium.launch({channel:'chrome', headless:true, args:['--mute-audio','--disk-cache-size=1']});
try {
  const page = await browser.newPage({viewport:{width:1200,height:750}});
  const errors = [];
  page.on('pageerror',e=>errors.push(e.message));
  await page.addInitScript(() => {
    const state = window.bambiDrive = { prepared:new Set(), transfer:0, frames:new Map(), basis:'', entered:0, trigger:[] };
    const gl = WebGLRenderingContext.prototype, set = gl.uniform4fv;
    gl.uniform4fv = function(loc,val,...rest) {
      if (this.canvas.closest('.bambi-sky')) state.basis = Array.from(val,n=>n.toFixed(4)).join(',');
      return set.call(this,loc,val,...rest);
    };
    const draw = CanvasRenderingContext2D.prototype.drawImage;
    CanvasRenderingContext2D.prototype.drawImage = function(source,...args) {
      const result = draw.call(this,source,...args);
      if (source instanceof HTMLVideoElement && source.closest('.bi-dome-stage') && source.readyState >= 2) {
        const old = state.frames.get(source);
        if (old !== source.currentTime) state.frames.set(source,source.currentTime);
      }
      return result;
    };
    const observe = () => {
      for (const video of document.querySelectorAll('#l-ending > [aria-hidden="true"] video')) {
        if (video.paused && video.readyState >= 2) state.prepared.add(video);
      }
      const root=document.querySelector('.bi-dome-stage');
      if (root && !state.entered) {
        state.entered=performance.now();
        state.transfer=[...root.querySelectorAll('video')].filter(node=>state.prepared.has(node)).length;
        const word=root.querySelector('.bi-finale__title');
        new MutationObserver(()=>state.trigger.push([!word.hidden,performance.now()])).observe(word,{attributes:true,attributeFilter:['hidden']});
      }
      requestAnimationFrame(observe);
    };
    requestAnimationFrame(observe);
  });
  await page.goto(url);
  const f=page.frames().find(frame=>frame.url().includes('/fuzz/index.html')) || await page.waitForEvent('framenavigated',frame=>frame.url().includes('/fuzz/index.html'));
  if (live) await f.locator('[data-pick=signin]').click();
  await f.waitForFunction(()=>!!window.__bimbodollProbe,{},{timeout:45000});
  assert.equal(await page.getByRole('button',{name:'Enter the bubble'}).count(),0);
  let held=0;
  for(let i=0;i<480;i++) {
    const p=await f.evaluate(()=>{const p=window.__bimbodollProbe?.();return {held:p?.held?.length||0,sky:!!document.querySelector('.bi-dome-stage'),popped:p?.popped};});
    held=Math.max(held,p.held);
    if (p.sky) break;
    if (!p.popped) await page.mouse.click(130,280);
    await page.waitForTimeout(230);
  }
  assert.ok(held >= 4, 'the original game objects were swallowed');
  await f.locator('.bambi-sky[data-sky-renderer]').waitFor({timeout:4000});
  assert.ok(await f.evaluate(()=>window.bambiDrive.transfer)>=2,'at least two previously decoded clips are already in the first interior frame');
  const before=await f.evaluate(()=>window.bambiDrive.basis);
  await page.mouse.move(430,355);await page.mouse.move(660,386,{steps:12});await page.waitForTimeout(80);
  assert.notEqual(await f.evaluate(()=>window.bambiDrive.basis),before,'mouse movement alone looks around');
  const pill=f.locator('.bi-dome-controls');
  await f.locator('.bi-dome-controls[data-idle]').waitFor();
  await page.waitForTimeout(750);
  const opacity=()=>pill.evaluate(n=>Number(getComputedStyle(n).opacity));
  assert.ok(await opacity()<.7,'the pill dims');
  await pill.hover();await page.waitForTimeout(700);
  assert.ok(await opacity()>.95,'hover restores the pill');
  assert.ok(await f.evaluate(()=>window.bambiDrive.frames.size)>=2,'two different videos reached the sky atlas');
  await f.locator('#wake').click();
  await f.locator('#pause').waitFor({state:'visible'});
  await f.locator('#resume').click();
  await f.locator('#l-credits').waitFor({timeout:46000});
  const stats=await f.evaluate(()=>({prepared:window.bambiDrive.prepared.size,transferred:window.bambiDrive.transfer,trigger:window.bambiDrive.trigger,entered:window.bambiDrive.entered}));
  assert.equal(stats.trigger.length,2);
  assert.ok(stats.trigger[0][1]-stats.entered>=34000,'30 s of roaming plus the closing fade');
  assert.ok(stats.trigger[1][1]-stats.trigger[0][1]>=265 && stats.trigger[1][1]-stats.trigger[0][1]<470,'about 300 ms on black');
  assert.match(await f.locator('.rr-line').first().textContent(),/Congratulations, Bambi/);
  assert.deepEqual(errors,[]);
  console.log(JSON.stringify({live,held,prepared:stats.prepared,firstFrameClips:stats.transferred,triggerMs:Math.round(stats.trigger[1][1]-stats.trigger[0][1]),credits:true}));
} finally { await browser.close(); }
