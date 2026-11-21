import {createRequire} from 'node:module';
import {execFile} from 'node:child_process';
import {promisify} from 'node:util';
import {mkdir,writeFile,unlink} from 'node:fs/promises';
import {resolve,join} from 'node:path';
import assert from 'node:assert/strict';
import {installGameAudio,beginGameAudio,finishGameAudio,beginTabVideo,muxWalk} from './expo-film.mjs';
const exec=promisify(execFile),out=resolve(process.argv[2]),base=process.env.FUZZ_BASE||'http://127.0.0.1:8941/fuzz/';
if(!['localhost','127.0.0.1','[::1]'].includes(new URL(base).hostname))throw Error('Use a local preview.');
const {chromium}=createRequire('C:/wt-fe/e2e/')('playwright');
const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files/Google/Chrome/Application/chrome.exe',
 args:['--mute-audio','--force_low_power_gpu']});
const report={source:'Ordinary game master during real keyboard walks. Each camera cut moves to the next surface.',shots:[],errors:[]};
await mkdir(out,{recursive:true});
try{
 const ctx=await browser.newContext({viewport:{width:1280,height:720}});await installGameAudio(ctx);
 const page=await ctx.newPage();page.on('pageerror',e=>report.errors.push(e.message));
 await page.goto(base+'dev/play-ending.html?end=ll_end&pictures=none');
 await page.getByRole('button',{name:'Play Llama Expo 2037'}).click();
 const f=page.frames().find(fr=>fr.url().includes('/fuzz/index.html'));await f.locator('.expo-worm #btn').click();
 await f.waitForFunction(()=>window.__expo?.state().ready&&!document.querySelector('.expo-crossing'),null,{timeout:120000});
 for(const [room,at,surface] of [['hall',[0,0,13.1],'stone'],['hall',[3.4,0,11.7],'carpet'],
   ['hall',[10.7,3.4,5.15],'wood'],['catwalk',[-22,11.95,7.4],'metal']]){
  await f.evaluate(async({room,at})=>{const e=__expo;await e.go(room,at,0);e.root.focus();},{room,at});
  await f.waitForTimeout(280);
  const raw=join(out,surface+'-raw.mp4'),audio=join(out,surface+'-raw.webm'),clip=join(out,surface+'.mp4');
  const stop=await beginTabVideo(page,raw),audioAt=await beginGameAudio(f);
  await page.waitForTimeout(210);await page.keyboard.down('w');
  const trace=await f.evaluate(async()=>{
   const e=__expo,trace=[],start=performance.now(),feet=e.state().feet;
   let last=e.footfalls().count;
   while(performance.now()-start<2200){
    await new Promise(requestAnimationFrame);
    const s=e.footfalls();if(s.count!==last){last=s.count;trace.push({ms:performance.now()-start,feet:e.state().feet,...s.last});}
   }
   return {start:feet,end:e.state().feet,trace};
  });
  await page.keyboard.up('w');await f.waitForTimeout(500);await finishGameAudio(f,audio);await stop();
  await muxWalk(raw,audio,clip,2.7,(audioAt-stop.startedAt)/1000);await unlink(raw);await unlink(audio);
  report.shots.push({surface,room,...trace});
  assert(trace.trace.length>=3,surface+' had too few grounded steps');
  assert(trace.trace.every(s=>s.surface===surface&&s.body==='assets/audio/sfx/thud.mp3'),surface+' changed underfoot');
 }
 const sequence=join(out,'surfaces.txt');
 await writeFile(sequence,report.shots.map(s=>"file '"+s.surface+".mp4'").join('\n'));
 await exec('ffmpeg',['-v','error','-y','-f','concat','-safe','1','-i',sequence,'-t','10.8','-c','copy',
  '-movflags','+faststart',join(out,'footsteps.mp4')]);await unlink(sequence);
 assert.deepEqual(report.errors,[]);
}finally{await browser.close();await writeFile(join(out,'footsteps.json'),JSON.stringify(report,null,2));}
