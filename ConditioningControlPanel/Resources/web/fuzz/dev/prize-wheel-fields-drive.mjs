// Verify actual material motion, independent rotor motion, effects and accessibility.
import {createRequire} from 'node:module';
import {resolve} from 'node:path';
import {mkdirSync} from 'node:fs';
import assert from 'node:assert/strict';
const {chromium}=createRequire('C:/wt-fe/e2e/package.json')('playwright');
const base=process.env.FUZZ_URL||'http://localhost:8807/fuzz/index.html';
const shots=process.argv[2] && resolve(process.argv[2]);
if(shots) mkdirSync(shots,{recursive:true});
const browser=await chromium.launch({channel:'chrome',args:['--mute-audio']});
try {
  for(const phone of [false,true]) {
    const name=phone?'phone':'desktop';
    const context=await browser.newContext({viewport:phone?{width:390,height:844}:{width:1280,height:850},hasTouch:phone});
    const page=await context.newPage(), errors=[];
    page.on('pageerror',err=>errors.push(err.message));
    await page.goto(base+'?buy=wheel&pictures=none');
    await page.waitForFunction(()=>window.fuzz?.engine.scopes.get('wheel')?.ret.show);
    const target=page.locator('.tc-prize-wheel');
    await page.evaluate(async()=>{
      const {SECTORS}=await import('./sim/prize-wheel.js');
      const {machine}=await import('./assets/prize-wheel/geometry.js');
      const assets=new Image();assets.src='./assets/prize-wheel/fields.webp';await assets.decode();
      const engine=fuzz.engine, step=engine.step.bind(engine);engine.step=()=>{};
      engine.scopes.get('wheel').ret.show();
      const root=document.querySelector('.tc-prize-wheel'), face=root.querySelector('.tc-wheel-face');
      const ctx=face.getContext('2d'), aura=root.querySelector('.tc-wheel-aura').getContext('2d');
      const printed = new Set(), fillText = ctx.fillText.bind(ctx);
      ctx.fillText = (text,...args) => { printed.add(text); fillText(text,...args); };
      const scale=face.width/machine.width;
      const sectors=SECTORS.map(s=>{
        const indexes=[], angle=Number(root.dataset.wheelAngle);
        for(let y=0;y<face.height;y+=2) for(let x=0;x<face.width;x+=2) {
          const dx=x/scale-machine.center[0],dy=y/scale-machine.center[1];
          const theta=((Math.atan2(dx,-dy)*180/Math.PI-angle)%360+360)%360;
          const radius=Math.hypot(dx,dy);
          if(radius>32 && radius<94 && theta>s.start+1.4 && theta<s.end-1.4) indexes.push((y*face.width+x)*4);
        }
        return {id:s.id,indexes};
      });
      const pix=()=>ctx.getImageData(0,0,face.width,face.height).data;
      let saved=pix();
      const signed=a=>Math.atan2(Math.sin(a),Math.cos(a));
      let first=true,path=[],rootAngle=null,tipAngle=null;
      const clear=aura.clearRect.bind(aura),begin=aura.beginPath.bind(aura),line=aura.lineTo.bind(aura),close=aura.closePath.bind(aura);
      aura.clearRect=(...args)=>{first=true;clear(...args);};
      aura.beginPath=()=>{path=[];begin();};
      aura.lineTo=(x,y)=>{if(first)path.push([x,y]);line(x,y);};
      aura.closePath=()=>{
        if(first && path.length===106) {
          first=false; rootAngle=Math.atan2(path[0][1],path[0][0]);tipAngle=Math.atan2(path[52][1],path[52][0]);
        }
        close();
      };
      window.fieldTest={
        printed,
        step(ms){while(ms>0){const dt=Math.min(ms,16);step(dt);ms-=dt;}},
        sample(){saved=pix();},
        differences(){
          const current=pix();
          return sectors.map(({id,indexes})=>{
            const changed=indexes.filter(i=>Math.max(...[0,1,2].map(k=>Math.abs(saved[i+k]-current[i+k])))>=10).length/indexes.length;
            return [id,Math.round(changed*100)];
          });
        },
        aura(){return [rootAngle,signed(tipAngle-rootAngle)];},
        blankAura(){return !aura.getImageData(0,0,aura.canvas.width,aura.canvas.height).data.some(Boolean);},
        snapshot(){return face.toDataURL();},
      };
      window.fieldTest.step(96);
    });
    const step=ms=>page.evaluate(ms=>fieldTest.step(ms),ms);
    const snap=()=>page.evaluate(()=>fieldTest.snapshot());
    const angle=()=>target.getAttribute('data-wheel-angle');
    const hold=async(label)=>{const before=await snap();await step(510);assert.equal(await snap(),before,label);};
    const checkFields=async(label)=>{
      const position=await angle();
      await page.evaluate(()=>fieldTest.sample());
      await step(670);
      const diff=await page.evaluate(()=>fieldTest.differences());
      for(const [id,moved] of diff)assert.ok(moved>8,label+' '+id+': '+moved+'% pixels changed');
      assert.equal(await angle(),position,label+' rotor remained fixed');
      console.log(name,label,JSON.stringify(diff));
    };
    await checkFields('idle');
    const words=await page.evaluate(()=>[...fieldTest.printed]);
    for (const trigger of ['FOCUS','BLANK','DEEPER','RELAX']) assert.ok(words.includes(trigger),trigger+' printed before a spin');
    if(shots)await target.screenshot({path:resolve(shots,name+'-idle.png')});
    await page.evaluate(()=>fuzz.ui.pause());await hold('pause');await page.evaluate(()=>fuzz.ui.resume());
    await page.evaluate(()=>document.querySelector('.tc-prize-wheel').closest('.dk-b').dock.setVisible(false));
    await hold('hidden dock');
    await page.evaluate(()=>fuzz.engine.scopes.get('wheel').ret.show());
    await page.evaluate(()=>fuzz.ui.setInit({gentle:true}));await step(20);await hold('gentle');
    await page.evaluate(()=>fuzz.ui.setInit({gentle:false}));
    await page.emulateMedia({reducedMotion:'reduce'});await step(20);await hold('reduced');
    await page.emulateMedia({reducedMotion:'no-preference'});
    await target.locator('.tc-wheel-spin').evaluate(b=>b.click());
    await step(360);const [initialRoot]=await page.evaluate(()=>fieldTest.aura());
    await step(32);const [laterRoot,curl]=await page.evaluate(()=>fieldTest.aura());
    assert.ok(Math.atan2(Math.sin(laterRoot-initialRoot),Math.cos(laterRoot-initialRoot))>0,'root rotates clockwise');
    assert.ok(curl < -1.9,'tip trails the moving root');
    if(shots)await target.screenshot({path:resolve(shots,name+'-spin.png')});
    await step(3400);
    assert.ok(await page.evaluate(()=>fieldTest.blankAura()),'aura clears after landing');
    assert.ok(await target.locator('.tc-wheel-spin').isDisabled());
    await checkFields('cooldown');
    assert.deepEqual(errors,[]);
    await context.close();
  }
} finally { await browser.close(); }
