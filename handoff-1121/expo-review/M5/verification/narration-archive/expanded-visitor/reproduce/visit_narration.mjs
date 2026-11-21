import {createRequire} from 'node:module';
import {mkdir,readFile,writeFile} from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {createHash} from 'node:crypto';
import {gunzipSync} from 'node:zlib';
const requireLocal=createRequire(import.meta.url);
let playwright;
try{playwright=requireLocal(process.env.PW_DIR||'playwright');}
catch{playwright=createRequire('C:/wt-fe/e2e/')('playwright');}
const {chromium}=playwright;
const here=path.dirname(fileURLToPath(import.meta.url)),work=path.resolve(here,'..');
const root=path.resolve(process.argv[2]||path.join(work,'expo-review/M5/verification/narration-archive'));
const route=JSON.parse(await readFile(process.argv[3]||path.join(here,'narration-path.json'),'utf8'));
const promenade=route.some(row=>row.dwell);
const book=JSON.parse(await readFile(process.argv[4]||path.join(root,'media-audit.json'),'utf8'));
const metadata=new Map(book.audio.map(row=>[row.id,row]));
const proof={entry:'ordinary paid ending preview and portal entry',movement:'W input through the game collision controller, with camera steering',begun:new Date().toISOString(),visits:[],errors:[],badResponses:[],requests:[],replays:[]};
const base=process.env.FUZZ_BASE||(promenade?'http://127.0.0.1:8941/fuzz/':'http://127.0.0.1:8937/fuzz/');
await mkdir(path.join(root,'stations'),{recursive:true});
if(promenade)await mkdir(path.join(root,'sequences'),{recursive:true});
const snap=async()=>writeFile(path.join(root,'visitor-route.json'),JSON.stringify(proof,null,2));
const browser=await chromium.launch({executablePath:process.env.CHROME_EXE||'C:/Program Files/Google/Chrome/Application/chrome.exe',headless:true,
  args:['--mute-audio','--autoplay-policy=no-user-gesture-required','--disable-background-timer-throttling','--force_low_power_gpu']});
try{
  const pg=await browser.newPage({viewport:{width:1040,height:640}});
  const geometry=[];proof.browserGeometry={};
  pg.on('response',response=>{
    const name=new URL(response.url()).pathname.split('/').at(-1);
    if(!/^(hall|atrium|chronology|forum|commons|aquarium|shop|hoof|habitat|twelve)(?:-collision|-[a-f\d]+)?\.glb(?:\.gz)?$/.test(name))return;
    geometry.push((async()=>{
      const payload=await response.body(),bytes=payload[0]===31&&payload[1]===139?gunzipSync(payload):payload;
      proof.browserGeometry[name]={sha256:createHash('sha256').update(bytes).digest('hex'),bytes:bytes.length};
    })().catch(e=>proof.errors.push('GLB response receipt: '+String(e))));
  });
  pg.on('pageerror',e=>proof.errors.push(e.message));
  pg.on('response',r=>{const url=new URL(r.url());if(url.pathname.includes('/narration/')){const rel=url.pathname.split('/narration/')[1];if(r.status()>=400)proof.badResponses.push([rel,r.status()]);else if(!proof.requests.includes(rel))proof.requests.push(rel);}});
  await pg.goto(base+'dev/play-ending.html?end=ll_end&pictures=none&herd=12');
  const buy=pg.getByRole('button',{name:'Play Llama Expo 2037'});await buy.waitFor();await buy.click();
  const frame=pg.frames().find(f=>f.url().includes('/fuzz/index.html'));await frame.locator('.expo-worm #btn').click();
  await frame.waitForFunction(()=>window.__expo?.state().ready&&Array.isArray(__expo.narrationState().stations),null,{timeout:120000});
  const started=Date.now();
  await frame.evaluate(async()=>{
    const T=await import('three'),ex=__expo,ray=new T.Ray(),delta=new T.Vector3();
    const data=window.__archiveVisit={voices:[],blocked:[],seen:new Set(),queued:[],records:{},maxQueue:0,heard:[],rooms:[],peakSimultaneousNarrators:0};
    const sample=()=>{
      const n=ex.narrationState(),v=n.voice;const all=n.stations,at=ex.walker.feet.toArray();const now=performance.now();
      data.heard=n.heard;data.finished=n.finished;
      const location=ex.state().zone;if(data.rooms.at(-1)?.id!==location)data.rooms.push({id:location,at:now,feet:at});
      data.peakSimultaneousNarrators=Math.max(data.peakSimultaneousNarrators,Number(!!v.id)+Number(!!n.pa.id));
      if(v.id&&data.voices.at(-1)?.id!==v.id)data.voices.push({id:v.id,began:now,feet:at});
      if(v.queue.length){data.maxQueue=Math.max(data.maxQueue,v.queue.length);const q=v.queue.join(' ');if(data.queued.at(-1)?.ids!==q)data.queued.push({at:now,playing:v.id,ids:q});}
      if(v.id?.startsWith('ev_')){
        const r=data.records[v.id]??={subtitles:[],slidePaths:[],textures:[],lamps:[],peakZoom:0,full:false,mediaSeconds:0};
        if(v.subtitle&&!r.subtitles.includes(v.subtitle))r.subtitles.push(v.subtitle);r.mediaSeconds=Math.max(r.mediaSeconds,v.seconds);
        const s=all.find(s=>s.id===v.id),project=s?.project;
        if(project){if(project.slide&&!r.slidePaths.includes(project.slide))r.slidePaths.push(project.slide);r.full||=project.full;
          if(project.active&&project.full&&project.texture&&r.textures.at(-1)?.file!==project.texture)
            r.textures.push({file:project.texture,seconds:v.seconds,duration:v.duration,fraction:v.seconds/v.duration});
          if(project.active&&project.lamp>.25&&!r.lamps.length)r.lamps.push(project.lamp);r.peakZoom=Math.max(r.peakZoom,project.zoom);}
      }
      for(const s of all){
        if(!s.id.startsWith('ev_')||data.seen.has(s.id)||n.heard.includes(s.id))continue;
        delta.fromArray(s.pos).sub(ex.camera.position);const distance=delta.length();if(distance>3.5||distance<.1)continue;
        ray.origin.copy(ex.camera.position);ray.direction.copy(delta).normalize();
        if(ex.walker.blocked(ray,distance)){data.seen.add(s.id);data.blocked.push({id:s.id,distance,feet:at,at:now,heard:false});}
      }
    };
    window.__archiveTimer=setInterval(sample,70);sample();
    const el=ex.renderer.domElement;el.tabIndex=0;el.focus();
  });
  proof.admission=await frame.evaluate(()=>({feet:__expo.state().feet,zone:__expo.state().zone,narration:__expo.narrationState()}));
  await pg.screenshot({path:path.join(root,'admission.jpg'),type:'jpeg',quality:87});await snap();console.log('Admission captured',proof.admission.feet);

  async function walk(points){
    const first=await frame.evaluate(p=>{
      const c=__expo.camera,w=__expo.walker,delta=p.find(pt=>Math.hypot(pt[0]-w.feet.x,pt[1]-w.feet.z)>.19);
      if(delta)c.rotation.set(0,Math.atan2(w.feet.x-delta[0],w.feet.z-delta[1]),0,'YXZ');
      __expo.renderer.domElement.focus();return w.feet.toArray();
    },points);
    await pg.keyboard.down('w');
    try{return await frame.evaluate(async({points,first})=>{
      const ex=__expo,w=ex.walker,c=ex.camera;let metres=0,lastPos=w.feet.clone(),idx=0,best=1e9,stamp=performance.now();const start=stamp;
      while(idx<points.length){
        await new Promise(resolve=>requestAnimationFrame(resolve));const now=performance.now(),p=points[idx],dx=p[0]-w.feet.x,dz=p[1]-w.feet.z,d=Math.hypot(dx,dz);
        metres+=w.feet.distanceTo(lastPos);lastPos.copy(w.feet);
        if(d<(idx===points.length-1?.21:.27)){idx++;best=1e9;stamp=now;continue;}
        if(d<best-.025){best=d;stamp=now;}
        if(now-stamp>2000||now-start>40000)return {complete:false,idx,points:points.length,target:p,first,feet:w.feet.toArray(),metres};
        c.rotation.set(0,Math.atan2(-dx,-dz),0,'YXZ');
      }
      return {complete:true,first,feet:w.feet.toArray(),metres};
    },{points,first});}finally{await pg.keyboard.up('w');}
  }
  async function looking(p){await frame.evaluate(p=>__expo.lookAt(...p),p);}
  async function state(){return frame.evaluate(()=>__expo.narrationState());}
  async function replay(row){
    const read=async()=>{await looking(row.replay);await frame.waitForTimeout(280);
      return frame.evaluate(()=>__expo.root.querySelector('.expo-prompt')?.textContent||'');};
    let prompt=await read();const approaches=[];
    if(!prompt.toLowerCase().includes('replay')){
      const [rx,,rz]=row.replay,[cx,,cz]=row.screen;
      const nx=rx-cx,nz=rz-cz,length=Math.hypot(nx,nz)||1;
      const normal=row.normal?[row.normal[0],row.normal[2]]:Math.abs(nx)>Math.abs(nz)?[Math.sign(nx),0]:[0,Math.sign(nz)];
      // A visitor can approach the front of the pedestal and sidestep the speaker grille.
      const aim=normal.some(Boolean)?normal:[nx/length,nz/length];
      for(const [forward,side] of [[1.12,0],[.83,.28],[1.30,-.36],[1.18,.47]]){
        const point=[rx+aim[0]*forward+aim[1]*side,rz+aim[1]*forward-aim[0]*side];
        approaches.push(await walk([point]));prompt=await read();
        if(prompt.toLowerCase().includes('replay'))break;
      }
    }
    if(!prompt.toLowerCase().includes('replay'))throw Error('No physical replay target for '+row.id+' '+JSON.stringify(approaches));
    const target=await frame.evaluate(()=>__expo.focusTarget?.());
    await pg.keyboard.press('e');await frame.waitForTimeout(220);await looking(row.screen);
    return {id:row.id,prompt,target,approaches,after:(await state()).voice.id};
  }
  for(const [i,row] of route.entries()){
    const record={id:row.id,position:i+1,zone:row.zone};proof.visits.push(record);
    record.walk=await walk(row.path);
    if(!record.walk.complete){
      // Make a small physical detour around an edge and return to the planned aisle.
      const [tx,tz]=row.path[record.walk.idx];const [x,,z]=record.walk.feet;const dx=tx-x,dz=tz-z,n=Math.hypot(dx,dz)||1;
      for(const side of [1,-1]){
        const retry=await walk([[x-dz/n*.44*side,z+dx/n*.44*side],...row.path.slice(record.walk.idx)]);
        (record.detours??=[]).push(retry);if(retry.complete)break;
      }
    }
    if(promenade){
      await frame.waitForFunction(id=>__expo.narrationState().stations.some(s=>s.id===id),row.id,{timeout:15000});
      Object.assign(row,await frame.evaluate(async id=>{
        const T=await import('three'),ex=__expo,stat=ex.narrationState().stations.find(s=>s.id===id);
        const centre=tag=>new T.Box3().setFromObject(ex.scene.getObjectByName('expo_'+tag+'_'+id),true).getCenter(new T.Vector3()).toArray();
        return {station:stat.pos,screen:centre('screen'),replay:centre('replay')};
      },row.id));
      record.realStation={position:row.station,screen:row.screen,replay:row.replay};
      if(!proof.gaze&&!(await state()).heard.includes(row.id)){
        const away=await frame.evaluate(p=>{const o=__expo.camera.position;return [2*o.x-p[0],o.y,2*o.z-p[2]];},row.screen);
        await looking(away);await frame.waitForTimeout(1150);
        const turnedAway=(await state()).heard.includes(row.id);
        await looking(row.screen);await frame.waitForTimeout(390);await looking(away);await frame.waitForTimeout(250);
        await looking(row.screen);const began=Date.now();await frame.waitForTimeout(480);
        const partial=(await state()).heard.includes(row.id);
        await frame.waitForFunction(id=>__expo.narrationState().heard.includes(id),row.id,{timeout:3500});
        proof.gaze={id:row.id,turnedAwayStarted:turnedAway,partialDwellStarted:partial,completedDwellMs:Date.now()-began};
        if(turnedAway||partial||proof.gaze.completedDwellMs<970)throw Error('Gaze dwell '+JSON.stringify(proof.gaze));
      }else{
        await looking(row.screen);await frame.waitForTimeout(1450);
      }
    }else{await looking(row.screen);await frame.waitForTimeout(260);}
    let n=await state();
    if(!n.heard.includes(row.id)){
      // A real foreground pier can block a station even inside its radius.
      const [x,y,z]=row.station;const [cx,,cz]=await frame.evaluate(()=>__expo.walker.feet.toArray());
      const dx=x-cx,dz=z-cz,len=Math.hypot(dx,dz)||1;
      for(const offset of [.42,-.70,.90]){
        const test=[cx+.38*dx/len-offset*dz/len,cz+.38*dz/len+offset*dx/len];
        (record.sightApproaches??=[]).push(await walk([test]));await looking(row.screen);await frame.waitForTimeout(promenade?1500:400);
        n=await state();if(n.heard.includes(row.id))break;
      }
    }
    n=await state();record.firstProximity=n.heard.includes(row.id);record.initialQueue=n.voice.queue;record.playingOnArrival=n.voice.id;
    if(!record.firstProximity)throw Error('Approach did not trigger '+row.id+' '+JSON.stringify(record));
    if(n.finished[row.id]&&!record.screenshot){record.replayedForPhotograph=await replay(row);proof.replays.push(record.replayedForPhotograph);}
    const threshold=n.finished[row.id]||0;let captured=false;
    const expected=metadata.get(row.id),photographed=new Set();record.sequence=[];
    const waitStart=Date.now();
    while(Date.now()-waitStart<220000){
      n=await state();const proj=n.stations.find(s=>s.id===row.id)?.project;
      if(n.voice.id===row.id&&n.voice.seconds>.7&&proj?.active&&proj.full&&proj.lamp>.6){
        const index=expected.slides.indexOf(proj.texture||proj.slide),settledAt=[0,.30,.70][index]||0;
        if(index>=0&&!photographed.has(index)&&n.voice.seconds>=settledAt*(n.voice.duration||0)+.80){
          const stem=String(i+1).padStart(2,'0')+'-'+row.id;
          const name=index===0?'stations/'+stem+'.jpg':'sequences/'+stem+'-'+(index+1)+'.jpg';
          await pg.screenshot({path:path.join(root,name),type:'jpeg',quality:88});
          record.sequence.push({file:name,texture:proj.texture,seconds:n.voice.seconds,subtitle:n.voice.subtitle});
          photographed.add(index);
          if(index===0){record.screenshot=name;record.atPhoto={voice:n.voice,project:proj};captured=true;}
        }
      }
      if((n.finished[row.id]||0)>threshold)break;
      await frame.waitForTimeout(180);
    }
    record.finished=n.finished[row.id]||0;record.secondsSinceAdmission=+( (Date.now()-started)/1000 ).toFixed(2);
    if(!record.finished||!captured||promenade&&photographed.size!==expected.slides.length)
      throw Error('Missing completed or photographed station '+row.id+' '+JSON.stringify(record));
    if(promenade){
      await frame.waitForFunction(({id,middle})=>{const p=__expo.narrationState().stations.find(s=>s.id===id)?.project;
        return p&&!p.active&&!p.full&&p.slide===middle&&p.resting===middle;},{id:row.id,middle:expected.slides[1]},{timeout:4500});
      record.restingAfter=(await state()).stations.find(s=>s.id===row.id).project;
    }
    await snap();console.log('Completed '+(i+1)+'/40 '+row.id+' at '+record.secondsSinceAdmission+'s');
  }
  const last=route.at(-1),ended=(await state()).finished[last.id];
  const rerun=await replay(last);proof.replays.push(rerun);
  await frame.waitForFunction(({id,ended})=>(__expo.narrationState().finished[id]||0)>ended,{id:last.id,ended},{timeout:65000});
  if(last.postRoute){
    proof.returnWalk=[];
    for(let i=0;i<last.postRoute.length;i+=12){
      const leg=await walk(last.postRoute.slice(i,i+12));proof.returnWalk.push(leg);
      if(!leg.complete)throw Error('Blocked on the physical exit to the main exhibition '+JSON.stringify(leg));
    }
    await snap();await pg.screenshot({path:path.join(root,'gift-shop-route-end.jpg'),type:'jpeg',quality:89});
  }
  proof.final=await frame.evaluate(()=>({...__archiveVisit,feet:__expo.walker.feet.toArray(),zone:__expo.walker.zone,seen:undefined,liveFinished:__expo.narrationState().finished}));
  await Promise.all(geometry);
  if(promenade&&proof.final.peakSimultaneousNarrators>1)proof.errors.push('More than one speech occupied a channel');
  for(const row of route){
    const actual=proof.final.records[row.id],expected=metadata.get(row.id);
    if(!actual?.subtitles.includes(expected.subtitle)||!actual?.slidePaths.includes(expected.slides[0])||!actual?.full)
      proof.errors.push('Readback mismatch '+row.id);
    if(promenade)for(const [i,name] of expected.slides.entries()){
      const v=actual?.textures[i],at=[0,.30,.70][i];
      if(v?.file!==name||!(v.fraction>=at-.005&&v.fraction<=at+.045))proof.errors.push('Decoded frame timing '+row.id+' '+JSON.stringify(v));
    }
  }
  proof.wallSeconds=(Date.now()-started)/1000;proof.complete=proof.errors.length===0&&proof.visits.length===40&&proof.badResponses.length===0;
}catch(e){proof.errors.push(String(e));console.error(String(e));process.exitCode=1;}finally{await snap();await browser.close();}
