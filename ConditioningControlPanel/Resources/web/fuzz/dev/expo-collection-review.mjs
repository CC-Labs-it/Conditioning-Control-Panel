import {createRequire} from 'node:module';
import {mkdir,writeFile} from 'node:fs/promises';
import path from 'node:path';
import assert from 'node:assert/strict';
const {chromium}=createRequire('C:/wt-fe/e2e/')('playwright');
const out=path.resolve(process.argv[2]),base=process.env.FUZZ_BASE||'http://127.0.0.1:8937/fuzz/';
const route=new URL('dev/play-ending.html?end=ll_end&pictures=none',base).href;
await mkdir(out,{recursive:true});
const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files/Google/Chrome/Application/chrome.exe',args:['--mute-audio','--autoplay-policy=no-user-gesture-required']});
const report={base,errors:[],pieces:[],labels:{},requests:[]};
try {
  const page=await browser.newPage({viewport:{width:1440,height:900}});
  page.on('pageerror',e=>report.errors.push(e.message));
  page.on('response',r=>{if (/collection-|guest-(chess|classifieds)\.glb/.test(r.url())) report.requests.push([r.url(),r.status()]);});
  await page.goto(route);
  await page.getByRole('button',{name:'Play Llama Expo 2037'}).click();
  const frame=page.frames().find(f=>f.url().includes('/fuzz/index.html'));
  await frame.locator('.expo-worm #btn').click();
  await frame.waitForFunction(()=>window.__expo?.state().ready,null,{timeout:100000});
  const pieces=await frame.evaluate(async()=>(await (await fetch('assets/expo/collection/collection.json')).json()).pieces);
  await frame.waitForFunction(()=>__expo.narrationState().finished.pa_arrive_1>0,null,{timeout:45000});
  const readLabels=()=>frame.evaluate(()=>{
    const result={};
    __expo.scene.traverse(n=>{if(n.userData.collectionLabel)result[n.name]=n.userData.collectionLabel;});
    return result;
  });
  for (const row of pieces) {
    await frame.evaluate(async row=>{
      await __expo.go(row.zone,[row.camera[0],row.camera[1]-1.6,row.camera[2]]);
      __expo.lookAt(...row.target);
    },row);
    await frame.waitForFunction(row=>{
      const group=__expo.scene.getObjectByName('collection_'+row.id);
      return group && (!row.anchor || group.getObjectByName(row.anchor.name)?.children.length);
    },row,{timeout:30000});
    await page.waitForTimeout(600);
    const sample=await frame.evaluate(async row=>{
      const T=await import('three'),e=__expo,r=e.scene.getObjectByName('collection_'+row.id),eye=e.camera.position.clone();
      const present=new T.Box3().setFromObject(r,true),axes=new T.Vector3(),feet=e.walker.feet.toArray();
      const physical=[];
      e.scene.traverse(n=>{if(n.isMesh&&n.visible&&n.geometry?.attributes?.position&&
        !n.material?.transparent&&!n.material?.isShaderMaterial&&!n.userData.fx) physical.push(n);});
      const ray=new T.Raycaster(eye,new T.Vector3().fromArray(row.target).sub(eye).normalize(),.06,8);
      function visibleParent(n){for(;n;n=n.parent)if(!n.visible)return false;return true;}
      const first=ray.intersectObjects(physical,false).find(hit=>visibleParent(hit.object));
      const path=[];for(let n=first?.object;n;n=n.parent)path.push(n.name);
      let anchor=null;
      if(row.anchor){
        const slot=r.getObjectByName(row.anchor.name),insert=slot.children[0];
        const localBox=new T.Box3(),vertex=new T.Vector3(),inverse=slot.matrixWorld.clone().invert();
        insert.updateWorldMatrix(true,true);
        insert.traverse(n=>{
          if(!n.isMesh)return;
          const transform=inverse.clone().multiply(n.matrixWorld);
          const pos=n.geometry.getAttribute('position');
          for(let i=0;i<pos.count;i++)localBox.expandByPoint(vertex.fromBufferAttribute(pos,i).applyMatrix4(transform));
        });
        anchor={name:slot.name,world:slot.getWorldPosition(axes).toArray(),min:localBox.min.toArray(),max:localBox.max.toArray()};
      }
      return {id:row.id,zone:e.walker.zone,feet,eye:eye.toArray(),target:row.target,firstHit:path,
        threeBounds:[present.min.toArray(),present.max.toArray()],anchor,calls:e.renderer.info.render.calls};
    },row);
    report.pieces.push(sample);
    Object.assign(report.labels,await readLabels());
    await page.screenshot({path:path.join(out,row.id+'.jpg'),type:'jpeg',quality:94});
    if(row.id==='drive'){
      report.wheel=await frame.evaluate(async()=>{
        const T=await import('three'),e=__expo,n=e.scene.getObjectByName('drive_handwheel');
        n.updateWorldMatrix(true,true);
        const center=n.localToWorld(new T.Vector3(.208,0,.092));
        e.lookAt(...center.toArray());
        const clips=e.scene.getObjectByName('drive_glass_clamps');
        return {center:center.toArray(),target:e.focusTarget(),before:n.rotation.z,
          clampWorldBounds:clips?new T.Box3().setFromObject(clips,true).getSize(new T.Vector3()).toArray():null};
      });
      await page.keyboard.press('e');await page.waitForTimeout(450);
      report.wheel.after=await frame.evaluate(()=>[__expo.scene.getObjectByName('drive_handwheel').rotation.z,
        ...[0,1,2].map(i=>__expo.scene.getObjectByName('drive_gear_'+i).rotation.z)]);
    }
  }
  for (const [zone,at] of [['hoof',[-16.93,0,5.97]],['habitat',[16.12,0,9.17]]]) {
    await frame.evaluate(async([id,feet])=>{await __expo.go(id,feet);},[zone,at]);
    await frame.waitForFunction(zone=>__expo.scene.getObjectByName('collection_finish_'+zone),zone);
    Object.assign(report.labels,await readLabels());
  }
  await frame.evaluate(async()=>{await __expo.go('shop',[20.84,0,-9.75]);});
  for(const [i,row] of pieces.entries()){
    const sample=report.pieces[i];assert.equal(sample.zone,row.zone);
    assert(Math.hypot(...sample.eye.map((x,j)=>x-row.camera[j]))<.10,'illegal eye for '+row.id);
    if(row.anchor){
      const {min,max}=sample.anchor,[w,h,d]=row.anchor.maximum;
      assert(min[0]>=-w/2-.004&&max[0]<=w/2+.004&&min[2]>=-d/2-.004&&max[2]<=d/2+.004,'insert extends off shelf '+row.id);
      assert(min[1]>=-.005&&max[1]<=h+.004,'insert height '+row.id);
    }
  }
  assert.equal(report.wheel.target?.owner,'drive_handwheel');
  assert(report.wheel.clampWorldBounds?.every(n=>n>0));
  assert.equal(Object.keys(report.labels).length,8);
  assert(report.wheel.after[0]-report.wheel.before>.5);
  assert(Math.abs(report.wheel.after[1]/report.wheel.after[0]-26/16)<.00001);
  assert(Math.abs(report.wheel.after[2]/report.wheel.after[0]+26/24)<.00001);
  assert.equal(report.errors.length,0);report.passed=true;
} catch(error){report.errors.push(error.stack);process.exitCode=1;}
finally{await writeFile(path.join(out,'collection.json'),JSON.stringify(report,null,2));await browser.close();}
