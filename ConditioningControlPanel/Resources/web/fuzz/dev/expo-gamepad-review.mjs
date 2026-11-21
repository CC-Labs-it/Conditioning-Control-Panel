import assert from 'node:assert/strict';
import {mkdir, writeFile} from 'node:fs/promises';
import {createRequire} from 'node:module';
import path from 'node:path';

const {chromium} = createRequire(process.env.PW_DIR || 'C:/wt-fe/e2e/')('playwright');
const base = new URL(process.env.FUZZ_BASE || 'http://127.0.0.1:8937/fuzz/');
assert(['localhost', '127.0.0.1', '[::1]'].includes(base.hostname));
const out = path.resolve(process.argv[2] || 'dev-captures/expo-gamepad');
await mkdir(out, {recursive:true});
const report = {input:'Standard-mapped virtual gamepad through navigator.getGamepads()', checks:{}, errors:[]};

// Every same-origin frame sees the same device, as it would on the desktop.
function installVirtualPad() {
  const shared = window.top;
  if (!shared.museumTestPad) {
    shared.museumTestPad = {
      id:'Expo inspection controller', index:0, mapping:'standard', connected:false, timestamp:0,
      axes:[0,0,0,0], buttons:Array.from({length:17}, () => ({pressed:false,touched:false,value:0}))
    };
  }
  Object.defineProperty(navigator, 'getGamepads', {
    configurable:true, value:() => [shared.museumTestPad.connected ? shared.museumTestPad : null,null,null,null]
  });
}

const browser = await chromium.launch({
  executablePath:process.env.CHROME || 'C:/Program Files/Google/Chrome/Application/chrome.exe',
  headless:true,args:['--mute-audio','--autoplay-policy=no-user-gesture-required']
});
try {
  const page = await browser.newPage({viewport:{width:1200,height:720}});
  page.on('pageerror', error => report.errors.push(error.message));
  await page.addInitScript(installVirtualPad);
  await page.goto(new URL('dev/play-ending.html?end=ll_end&pictures=none', base).href);
  await page.bringToFront();
  const launcher = page.getByRole('button',{name:'Play Llama Expo 2037'});
  await launcher.waitFor({timeout:90000});
  const game = page.frames().find(f => f.url().includes('/fuzz/index.html'));
  assert(game);

  async function pad(change) {
    await page.evaluate(change => {
      const pad = window.museumTestPad;
      if ('connected' in change) pad.connected = change.connected;
      if (change.axes) pad.axes = change.axes;
      if ('button' in change) pad.buttons[change.button] = {
        pressed:change.down, touched:change.down, value:change.down ? 1 : 0
      };
      pad.timestamp = performance.now();
    },change);
  }
  async function press(button, hold=95) {
    await pad({button,down:true}); await page.waitForTimeout(hold);
    await pad({button,down:false}); await page.waitForTimeout(95);
  }
  const position = () => game.evaluate(() => ({
    feet:__expo.walker.feet.toArray(), yaw:__expo.camera.rotation.y, pitch:__expo.camera.rotation.x,
    roll:__expo.camera.rotation.z
  }));
  const distance = (a,b) => Math.hypot(b.feet[0]-a.feet[0],b.feet[2]-a.feet[2]);
  async function drive(axes,ms) {
    const from=await position(); await pad({axes}); await page.waitForTimeout(ms);
    await pad({axes:[0,0,0,0]}); return {from,to:await position()};
  }
  async function aim(room,feet,node) {
    await game.evaluate(async ({room,feet,node}) => {
      const {Box3,Vector3} = await import('three');
      await __expo.go(room,feet);
      const object=__expo.scene.getObjectByName(node);
      if(!object) throw Error('Missing '+node);
      __expo.lookAt(...new Box3().setFromObject(object,true).getCenter(new Vector3()).toArray());
    },{room,feet,node});
  }

  await pad({connected:true}); await page.waitForTimeout(250);
  await pad({button:0,down:true});
  await game.locator('.expo-worm #btn').waitFor({timeout:15000});
  await page.waitForTimeout(600);
  assert(await game.locator('.expo-worm #btn').isVisible(),'Holding purchase also entered the wormhole');
  await pad({button:0,down:false}); await page.waitForTimeout(150); await press(0);
  await game.waitForFunction(() => window.__expo?.state().ready,null,{timeout:90000});
  await page.waitForTimeout(250);
  report.checks.admission=await position();

  const drift=await drive([.09,-.09,.075,.075],350);
  assert(distance(drift.from,drift.to)<.01 && Math.abs(drift.to.yaw-drift.from.yaw)<.001);
  const slow=await drive([0,-.5,0,0],550), fast=await drive([0,-1,0,0],550);
  const partial=distance(slow.from,slow.to),full=distance(fast.from,fast.to);
  assert(partial>.22 && full>partial*1.4 && full>1.2,JSON.stringify({partial,full}));
  const turn=await drive([0,0,.8,-.6],400);
  assert(turn.to.yaw<turn.from.yaw-.15 && turn.to.pitch>turn.from.pitch+.05 && turn.to.roll===0);
  report.checks.analog={partial,full,yaw:turn.to.yaw-turn.from.yaw,pitch:turn.to.pitch-turn.from.pitch};

  const jumps=game.evaluate(async () => {
    const heights=[],start=performance.now(),floor=__expo.walker.feet.y;
    while(performance.now()-start<1250) {
      await new Promise(requestAnimationFrame); heights.push(__expo.walker.feet.y-floor);
    }
    return {peak:Math.max(...heights),end:heights.at(-1),
      takeoffs:heights.filter((v,i)=>v>.045 && (heights[i-1]||0)<=.045).length};
  });
  await press(0,950); const jump=await jumps;
  assert(jump.peak>.30 && jump.peak<.42 && jump.takeoffs===1 && Math.abs(jump.end)<.02,JSON.stringify(jump));
  report.checks.jump=jump;

  await aim('hall',[.04,0,-7.76],'press_main');
  const counter=() => game.evaluate(() => __expo.exhibits.state.presses);
  const before=await counter(); await press(2,650);
  assert.equal(await counter(),before+1); await press(2); assert.equal(await counter(),before+2);
  report.checks.switch=await counter();
  await page.screenshot({path:path.join(out,'first-press.jpg'),type:'jpeg',quality:88});

  await press(9); await game.locator('#pause').waitFor({state:'visible',timeout:5000});
  const frozen=await drive([0,0,1,0],300);
  assert(distance(frozen.from,frozen.to)<.01 && frozen.from.yaw===frozen.to.yaw);
  const initialFocus=await game.evaluate(()=>document.activeElement.id);
  assert.equal(initialFocus,'resume');
  await press(13); const nextFocus=await game.evaluate(()=>document.activeElement.id);
  assert(initialFocus!==nextFocus,JSON.stringify({initialFocus,nextFocus}));
  await page.screenshot({path:path.join(out,'pause-menu.jpg'),type:'jpeg',quality:88});
  await press(12); await press(0,300);
  await game.locator('#pause').waitFor({state:'hidden',timeout:5000});
  await page.waitForTimeout(100); assert(Math.abs((await position()).feet[1])<.02);
  report.checks.pause={initialFocus,nextFocus};

  await pad({axes:[1,0,0,0]}); await page.waitForTimeout(150); await pad({connected:false});
  await page.waitForTimeout(100); const stop=await position(); await page.waitForTimeout(250);
  assert(distance(stop,await position())<.015,'Disconnect left movement active');
  await page.keyboard.down('ArrowLeft'); await page.waitForTimeout(220); await page.keyboard.up('ArrowLeft');
  assert(distance(stop,await position())>.1,'Keyboard fallback after disconnect');
  await pad({axes:[0,0,0,0],connected:true}); await page.waitForTimeout(150);
  await press(1); await game.locator('#pause').waitFor({state:'visible',timeout:5000});
  await press(0); await game.locator('#pause').waitFor({state:'hidden',timeout:5000});
  report.checks.disconnectAndReconnect=true;

  async function shopPress(feet,look,expected) {
    await game.evaluate(async ({feet,look})=>{await __expo.go('shop',feet);__expo.lookAt(...look);},{feet,look});
    await game.waitForFunction(text=>document.querySelector('.expo-prompt')?.textContent.includes(text),expected);
    await press(2);
  }
  await shopPress([19.67,0,-10.14],[19.67,1.53,-11.53],'contract');
  for(let n=0;n<2;n++) {
    await shopPress([15.03,0,-12.14],[15.03,1.14,-13.93],'exit door'); await page.waitForTimeout(350);
  }
  await shopPress([22.77,0,-10.05],[22.77,1.37,-11.43],'Buy Exit');
  await shopPress([15.03,0,-12.14],[15.03,1.06,-13.75],'Go home');
  await game.locator('.expo-return-recap').waitFor({timeout:3000}); await press(0);
  await game.locator('.rr').waitFor({state:'attached',timeout:20000});
  report.checks.controllerReturnToRecap=true;
  assert.deepEqual(report.errors,[]); report.passed=true;
} catch(error) {report.errors.push(error.message);throw error;}
finally {
  await writeFile(path.join(out,'gamepad.json'),JSON.stringify(report,null,2)+'\n');
  await browser.close();
}
