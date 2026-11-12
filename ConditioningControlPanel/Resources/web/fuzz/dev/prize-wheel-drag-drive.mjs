import assert from 'node:assert/strict';
import { createRequire } from 'node:module';

const {chromium} = createRequire('C:/wt-fe/e2e/package.json')('playwright');
const base = process.env.FUZZ_URL || 'http://localhost:8807/fuzz/index.html';
const browser = await chromium.launch({channel:'chrome', args:['--mute-audio']});
try {
  for (const touch of [false,true]) {
    const context = await browser.newContext({hasTouch:touch, viewport:touch ? {width:390,height:844} : {width:1200,height:860}});
    const page = await context.newPage();
    const errors = [];
    page.on('pageerror', e => errors.push(e.message));
    const cdp = touch ? await context.newCDPSession(page) : null;
    async function pointer(type, point) {
      if (cdp) {
        const names = {down:'touchStart',move:'touchMove',up:'touchEnd',cancel:'touchCancel'};
        await cdp.send('Input.dispatchTouchEvent',{type:names[type],touchPoints:point?[{...point,id:1,radiusX:2,radiusY:2}]:[]});
      } else if (type === 'down') {
        await page.mouse.move(point.x,point.y); await page.mouse.down();
      } else if (type === 'move') await page.mouse.move(point.x,point.y);
      else await page.mouse.up();
    }
    for (const direction of [1,-1]) {
      await page.goto(base+'?preview=toys&focus=wheel&pictures=none');
      const root=page.locator('[data-widget="prize-wheel"]'), ring=root.locator('.tc-wheel-grip');
      await root.getByRole('button',{name:'Spin',exact:true}).waitFor();
      await page.evaluate(()=>{const e=fuzz.engine;window.advanceWheel=e.step.bind(e);e.step=()=>{};});
      const rect=await ring.boundingBox();
      function at(degrees) {
        const a=degrees*Math.PI/180;
        return {x:rect.x+rect.width*(.5+.34*Math.cos(a)), y:rect.y+rect.height*(.5+.34*Math.sin(a))};
      }
      const angle=async()=>Number(await root.getAttribute('data-wheel-angle'));
      const original=await angle();
      const deal=()=>page.evaluate(()=>fuzz.state.store.wheel.spin);
      const advance=ms=>page.evaluate(ms=>{while(ms>0){const dt=Math.min(50,ms);advanceWheel(dt);ms-=dt;}},ms);
      const initialBox=await root.boundingBox();

      await pointer('down',at(165)); await pointer('move',at(167)); await pointer('up');
      assert.equal(await deal()??null,null,'a tap or tiny nudge costs no spin');
      assert.equal(await angle(),original);
      await pointer('down',at(165)); await pointer('move',at(195));
      if(touch) await pointer('cancel');
      else {await page.evaluate(()=>fuzz.ui.pause());await pointer('up');await page.evaluate(()=>fuzz.ui.resume());}
      assert.equal(await angle(),original,'interrupted drag restores last settled angle');
      assert.equal(await deal()??null,null);

      const start=direction*165;
      await pointer('down',at(start));
      assert.notEqual(await ring.getAttribute('data-held'),null);
      for(let travel=10;travel<=110;travel+=10) {
        await pointer('move',at(start+travel*direction));
        await page.waitForFunction(expected => Math.abs(Number(document.querySelector('[data-wheel-angle]').dataset.wheelAngle) - expected) < 2.5, original+travel*direction, {timeout:1500});
      }
      assert.equal(await deal()??null,null,'no winner drawn while dragging');
      assert.deepEqual(await root.boundingBox(),initialBox,'the casing stayed still');
      await pointer('up');
      const plan=await deal();
      assert.equal(plan.motion,'fling');assert.equal(Math.sign(plan.end-plan.from),direction);
      assert.ok(Math.abs((((plan.from-original-direction*110)%360)+540)%360-180)<2.5);
      await advance(70);
      assert.ok((await angle()-plan.from)*direction>0,'released rotor coasts immediately');
      const prior=await page.evaluate(()=>fuzz.state.sources.wheel||0);
      await advance(plan.duration);
      const result=await page.evaluate(()=>fuzz.state.store.wheel.receipt);
      assert.equal(result.id,plan.id);
      assert.ok(Math.abs(await page.evaluate(()=>fuzz.state.sources.wheel||0)-prior-result.amount)<1e-6);
      await pointer('down',at(165));await pointer('move',at(220));await pointer('up');
      assert.equal(await deal(),null,'cooldown prevents another throw');
      assert.deepEqual(await root.boundingBox(),initialBox);

      // The physical plinth still moves the object, including on a touch hold.
      const stage=await root.locator('.tc-wheel-stage').boundingBox();
      const grip={x:stage.x+stage.width*.13,y:stage.y+stage.height*.90};
      await pointer('down',grip);
      if(touch) await page.waitForTimeout(500);
      for (let i=1;i<=6;i++) { await pointer('move',{x:grip.x+34*i/6,y:grip.y+22*i/6}); await page.waitForTimeout(20); }
      await pointer('up');
      const moved=await root.boundingBox();
      assert.ok(Math.hypot(moved.x-initialBox.x,moved.y-initialBox.y)>15,'plinth still drags the cabinet');
    }
    assert.deepEqual(errors,[]);
    console.log((touch?'Touch':'Mouse')+' passed clockwise and anticlockwise throws, interruption, cooldown and casing drag.');
    await context.close();
  }
}finally{await browser.close();}
