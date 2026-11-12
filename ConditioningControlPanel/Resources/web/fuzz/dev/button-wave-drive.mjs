// Run with FUZZ_URL pointing to the local game server.
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';

const { chromium } = createRequire('C:/wt-fe/e2e/package.json')('playwright');
const url = process.env.FUZZ_URL || 'http://localhost:8807/fuzz/index.html';
const browser = await chromium.launch({ channel:'chrome', args:['--mute-audio'] });
try {
  for (const phone of [false,true]) {
    const context = await browser.newContext({viewport:phone ? {width:390,height:844} : {width:1280,height:850}, hasTouch:phone});
    const page = await context.newPage(), errors = [];
    page.on('pageerror',e=>errors.push(e.message));
    await page.goto(url+'?fresh=1&buy=bubble');
    const door = page.getByRole('button',{name:'Button Shop',exact:true});
    await door.click();
    const shop = page.locator('.button-shop');
    await shop.getByRole('button',{name:'Wave',exact:true}).click();
    const sample = shop.locator('.wave-sample canvas');
    const ids = ['aurora','loom','mercury','mycelium','prism'];
    await page.evaluate(()=>{fuzz.engine.run.fuzz=0;});
    for (const id of ids) {
      await shop.locator('[data-wave-choice="'+id+'"]').click();
      assert.ok(await shop.locator('.wave-purchase button').isDisabled());
      const before = await page.evaluate(()=>({taps:fuzz.state.taps,spent:fuzz.state.spent}));
      await shop.getByRole('button',{name:'Test press'}).click();
      assert.deepEqual(await page.evaluate(()=>({taps:fuzz.state.taps,spent:fuzz.state.spent})),before);
    }
    await page.evaluate(()=>fuzz.add(15000));
    await shop.locator('.wave-purchase button').click();
    assert.equal(await page.evaluate(()=>fuzz.state.spent.wave_prism),12000);
    assert.equal(await page.evaluate(()=>!!fuzz.state.owned.a_hum),false,'cosmetic does not require Listen');
    await shop.getByRole('button',{name:'Done'}).click();
    await page.waitForFunction(()=>document.querySelector('[data-hum-wave][data-wave-style=prism]'));
    const pollImage = () => page.locator('[data-hum-wave]:visible').evaluate(c=>c.toDataURL());
    await page.emulateMedia({reducedMotion:'reduce'});
    await page.waitForTimeout(150);
    const quiet = await pollImage();
    const wave = page.locator('[data-hum-wave]:visible');
    const top = await wave.evaluate(c => c.style.top);
    const offset = await page.locator('#btn').evaluate(b => {
      const old = b.style.translate; b.style.translate = '0 32px'; return old;
    });
    await page.waitForTimeout(150);
    assert.equal(await wave.evaluate(c => c.style.top),top,'button skin travel does not move the ribbon anchor');
    await page.locator('#btn').evaluate((b,old) => { b.style.translate = old; },offset);
    await page.locator('#btn').focus(); await page.keyboard.press('Enter');
    await page.waitForTimeout(200);
    assert.equal(await pollImage(),quiet,'reduced motion freezes the live pattern across a real press');
    await page.emulateMedia({reducedMotion:'no-preference'});
    await page.waitForTimeout(180);
    assert.notEqual(await pollImage(),quiet);
    const saved = await page.evaluate(async()=>{
      const {parse,serialize}=await import('./sim/save.js');
      return parse(serialize(fuzz.state,fuzz.life)).run.store.bubble;
    });
    assert.equal(saved.waveStyle,'prism');
    assert.ok(saved.waveStyles.prism);
    await door.click();await shop.getByRole('button',{name:'Wave',exact:true}).click();
    assert.equal(await shop.locator('.wave-purchase button').textContent(),'Equipped');
    // Compare the same frame with and without a press, isolating it from idle motion.
    const changed = await sample.evaluate(async canvas=>{
      const {drawWave,WAVE_STYLES,waveFields}=await import('./ui/wave-ribbon.js');
      await waveFields(canvas.ownerDocument).decode();
      const c=document.createElement('canvas'); c.width=480;c.height=110;
      const g=c.getContext('2d'), counts=[];
      for (const style of WAVE_STYLES) {
        const opts={style:style.id,time:2,preview:true,hits:[]};
        drawWave(g,480,110,opts);const first=g.getImageData(0,0,480,110).data;
        drawWave(g,480,110,{...opts,hits:[{time:1.78,u:.5}]});const second=g.getImageData(0,0,480,110).data;
        let diff=0; for(let i=0;i<first.length;i+=4) if(Math.abs(first[i]-second[i])+Math.abs(first[i+3]-second[i+3])>32)diff++;
        counts.push(diff);
      }
      return counts;
    });
    assert.ok(changed.every(count=>count>180),'all five materials and Classic visibly respond to a press');
    assert.deepEqual(errors,[]); console.log(phone?'Phone wave checks passed.':'Desktop wave checks passed.');
    await context.close();
  }
} finally { await browser.close(); }
