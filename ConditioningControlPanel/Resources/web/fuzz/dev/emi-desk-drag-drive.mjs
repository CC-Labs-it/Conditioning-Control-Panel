import assert from 'node:assert/strict';
import { createRequire } from 'node:module';

const {chromium} = createRequire('C:/wt-fe/e2e/package.json')('playwright');
const base = process.env.FUZZ_URL || 'http://localhost:8807/fuzz/index.html';
const browser = await chromium.launch({channel:'chrome', args:['--mute-audio']});
try {
  for (const touch of [false, true]) {
    const viewport = touch ? { width:390, height:844 } : { width:1280, height:800 };
    const context = await browser.newContext({ viewport, hasTouch:touch });
    const page = await context.newPage();
    const cdp = touch && await context.newCDPSession(page);
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    const pointer = async (type, x, y) => {
      if (touch) {
        await cdp.send('Input.dispatchTouchEvent', {
          type: { down:'touchStart', move:'touchMove', up:'touchEnd', cancel:'touchCancel' }[type],
          touchPoints: type === 'up' || type === 'cancel' ? [] : [{ x, y, id:1, radiusX:2, radiusY:2 }],
        });
      } else if (type === 'down') {
        await page.mouse.move(x, y);
        await page.mouse.down();
      } else if (type === 'move') await page.mouse.move(x,y);
      else await page.mouse.up();
    };
    const root = page.locator('.xe[data-emi="1"]');
    const pet = root.getByRole('button',{name:'Pet EMI', exact:true});
    const arm = async () => {
      await page.waitForFunction(() => window.fuzz?.engine.scopes.has('mini_slots'));
      await page.evaluate(() => { fuzz.grant('em_hat'); });
      const fine = page.getByRole('button',{name:'Fine',exact:true});
      if (await fine.isVisible()) await fine.click();
      await root.waitFor();
      await page.waitForTimeout(650);
    };
    const pos = () => root.boundingBox();
    const closeTo = (a,b) => {
      assert.ok(Math.abs(a.x-b.x) < 2 && Math.abs(a.y-b.y) < 2, JSON.stringify({actual:a, expected:b, errors}));
    };
    const saved = () => page.evaluate(() => JSON.parse(localStorage.getItem('fuzz.preview.prefs.v1')).windowLayouts['em_head:mascot']);

    await page.goto(base+'?preview=combined&pictures=none');
    await arm();
    const start = await pos();
    const offset = { x: start.width/2, y: start.height/2 };
    const target = { x:Math.round(viewport.width*.58), y:Math.round(viewport.height*.32) };
    await pointer('down',start.x+offset.x,start.y+offset.y);
    await pointer('move',target.x+offset.x,target.y+offset.y);
    await pointer('up');
    closeTo(await pos(),target);
    closeTo(await saved(),target);
    assert.ok(await pet.isVisible(),'Dragging must leave the first-pet invitation in place');

    const hat = root.getByRole('button',{name:'Hat',exact:true});
    const initialHat = await root.locator('.xe-h').textContent();
    if (touch) await hat.tap(); else await hat.click();
    await page.waitForFunction(old => document.querySelector('.xe-h').textContent !== old,initialHat);
    closeTo(await pos(),target);
    await pointer('down',target.x+offset.x,target.y+offset.y);
    await pointer('up');
    assert.equal(await pet.count(),0,'An ordinary tap pets the mascot once');

    await pointer('down',target.x+offset.x,target.y+offset.y);
    await pointer('move',target.x+offset.x-50,target.y+offset.y-40);
    if (touch) await pointer('cancel');
    else {
      await page.evaluate(() => fuzz.engine.setPaused(true));
      await pointer('up');
      await page.evaluate(() => fuzz.engine.setPaused(false));
    }
    closeTo(await pos(),target);
    closeTo(await saved(),target);

    await page.reload();
    await arm();
    closeTo(await pos(),target);
    const naturalWidth=(await pos()).width;
    await page.evaluate(() => {
      window.sharedEmi = document.querySelector('.xe[data-emi]');
      document.querySelector('.xq-dock>.dk-b').dock.setVisible(true);
    });
    await root.locator('.dk-resize').focus();
    await page.keyboard.press('ArrowLeft');
    await page.waitForFunction(n => window.sharedEmi.offsetWidth > n, naturalWidth);
    closeTo(await pos(),target);
    await page.evaluate(() => fuzz.engine.switches.set('quiz',false));
    assert.equal(await root.locator('.xq-dock').count(),0);
    assert.equal((await pos()).width,naturalWidth);
    closeTo(await pos(),target);
    await page.evaluate(() => fuzz.engine.switches.set('quiz',true));
    await page.waitForFunction(n => window.sharedEmi.offsetWidth > n, naturalWidth);
    assert.equal(await page.evaluate(() => window.sharedEmi === document.querySelector('.xe[data-emi]')),true);
    closeTo(await pos(),target);
    await page.evaluate(() => { fuzz.engine.scopes.get('quiz').ret.close(); fuzz.grant('em_nap'); });
    await page.waitForFunction(() => document.querySelector('.xe .xe-f')?.dataset.m === 'sleep');
    await page.waitForTimeout(650);
    const asleep = await pos();
    await pointer('down',asleep.x+asleep.width/2,asleep.y+asleep.height/2);
    await pointer('up');
    await page.waitForTimeout(1650);
    closeTo(await pos(),target);
    assert.deepEqual(errors,[]);
    console.log('PASS',touch?'touch':'mouse','body drag, independent hat and pet, cancellation, saved position and resize, Questions toggle and nap return');
    await context.close();
  }
} finally {
  await browser.close();
}
