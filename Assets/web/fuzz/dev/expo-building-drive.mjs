import {createRequire} from 'node:module';
import {mkdir,writeFile} from 'node:fs/promises';
import path from 'node:path';
import {buildingRoute} from './expo-building-route.mjs';

const {chromium}=createRequire('C:/wt-fe/e2e/')(process.env.PW_DIR||'playwright');
const base=process.env.FUZZ_BASE||'http://127.0.0.1:8937/fuzz/';
if(!['localhost','127.0.0.1','[::1]'].includes(new URL(base).hostname))throw Error('Use a local preview.');
const directory=path.resolve(process.argv[2]||'./dev-captures/building-walk'),partial=process.argv.includes('--service');
const routes=buildingRoute(partial),proof={source:'staged game',partial,segments:[],transitions:[],jumps:[],faults:[]};
await mkdir(directory,{recursive:true});
const browser=await chromium.launch({headless:true,executablePath:process.env.CHROME_EXE||'C:/Program Files/Google/Chrome/Application/chrome.exe',
  args:['--mute-audio',...(process.env.GPU==='integrated'?['--force_low_power_gpu']:[])]});
try{
  const page=await browser.newPage({viewport:{width:1280,height:720}});page.on('pageerror',e=>proof.faults.push(e.message));
  await page.goto(base+'dev/play-ending.html?end=ll_end&pictures=none');
  const buy=page.getByRole('button',{name:'Play Llama Expo 2037'});await buy.waitFor();
  const frame=page.frames().find(f=>f.url().includes('/fuzz/index.html'));await buy.click();await frame.locator('.expo-worm #btn').click();
  await frame.waitForFunction(()=>window.__expo?.state().ready,null,{timeout:90000});
  proof.admission=await frame.evaluate(()=>__expo.state());
  if(!partial&&proof.admission.zone!=='atrium')throw Error('The full walk requires normal atrium admission.');
  if(partial)await frame.evaluate(()=>__expo.go('service',[12.55,0,32]));
  proof.transitions.push(partial?'service':proof.admission.zone);
  const began=Date.now();
  for(const [i,stage] of routes.entries()){
    const record=await frame.evaluate(async({stage})=>{
      const ex=__expo,w=ex.walker,cam=ex.camera;
      cam.rotation.x=0;
      const row={label:stage.label,start:w.feet.toArray(),metres:0,minY:w.feet.y,maxY:w.feet.y,points:0,frames:0,transitions:[w.zone],jumps:[],failed:null};
      for(const point of stage.points){
        const goal=point.goal||point;
        let reached=false,stalled=0,last=performance.now();const limit=last+120000;
        while(performance.now()<limit){
          await new Promise(r=>requestAnimationFrame(r));const now=performance.now(),dt=Math.min(.055,(now-last)/1000);last=now;
          const previous=w.feet.clone(),dx=goal[0]-previous.x,dz=goal[2]-previous.z,span=Math.hypot(dx,dz);
          if(span<.028){reached=!point.after&&Math.abs(w.feet.y-goal[1])<.19;break;}
          cam.rotation.y=Math.atan2(-dx,-dz);
          const stride=Math.min(3.8*dt,span);w.move(stride*dx/span,stride*dz/span);row.frames++;
          const travelled=previous.distanceTo(w.feet);
          if(row.transitions.at(-1)!==w.zone)row.transitions.push(w.zone);
          row.minY=Math.min(row.minY,w.feet.y);row.maxY=Math.max(row.maxY,w.feet.y);
          if(travelled>1.5){
            row.jumps.push({from:previous.toArray(),to:w.feet.toArray()});
            reached=!!point.after&&Math.hypot(...w.feet.toArray().map((n,i)=>n-point.after[i]))<.22;break;
          }
          row.metres+=travelled;
          stalled=travelled<.0008?stalled+dt:0;if(stalled>8)break;
        }
        if(!reached){row.failed={target:goal,after:point.after||null,feet:w.feet.toArray(),loaded:ex.state().loaded};break;}
        row.points++;
      }
      if(stage.aim){ex.lookAt(...stage.aim);row.aim=stage.aim;await new Promise(r=>requestAnimationFrame(r));}
      row.end=w.feet.toArray();row.metres=Number(row.metres.toFixed(3));row.surface=w.surface;return row;
    },{stage});
    proof.segments.push(record);for(const id of record.transitions)if(proof.transitions.at(-1)!==id)proof.transitions.push(id);
    proof.jumps.push(...record.jumps);proof.metres=Number(proof.segments.reduce((s,r)=>s+r.metres,0).toFixed(2));
    proof.wallSeconds=(Date.now()-began)/1000;
    await page.screenshot({type:'jpeg',quality:84,path:path.join(directory,String(i+1).padStart(2,'0')+'.jpg')});
    if(record.failed){proof.faults.push(stage.label+' '+JSON.stringify(record.failed));throw Error(proof.faults.at(-1));}
    await writeFile(path.join(directory,'walk.json'),JSON.stringify(proof,null,2));console.log(stage.label+' '+proof.metres+' m');
  }
  proof.end=await frame.evaluate(()=>__expo.state());proof.complete=true;
}finally{await writeFile(path.join(directory,'walk.json'),JSON.stringify(proof,null,2));await browser.close();}
