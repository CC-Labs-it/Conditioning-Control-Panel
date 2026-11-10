import {createRequire} from 'node:module';
// Cross-origin media fixture under the real host origins. No production data is fetched.
// Run from fuzz; optional BAMBI_BASE compares a revision, BAMBI_EVIDENCE saves captures.
import {readFileSync,writeFileSync} from 'node:fs';
import {execFileSync} from 'node:child_process';
import assert from 'node:assert/strict';
const require=createRequire(process.env.PW_ROOT||'C:/wt-fe/e2e/package.json');
const {chromium}=require('playwright');
const root=process.cwd();
const base=process.env.BAMBI_BASE||null;
const evidence=process.env.BAMBI_EVIDENCE||null;
const browser=await chromium.launch({channel:'chrome',headless:true,args:['--mute-audio','--use-angle=d3d11','--enable-gpu','--ignore-gpu-blocklist']});
try {
 const data=[];
 for(const [vw,vh,throttle] of [[1280,720,1],[390,844,4]]) for(const original of base?[true,false]:[false]) {
  const page=await browser.newPage({viewport:{width:vw,height:vh},isMobile:vw<700,hasTouch:vw<700});
  await page.route('https://app.cclabs.app/**',r=>r.fulfill({contentType:'text/html',body:'<meta name="viewport" content="width=device-width,initial-scale=1"><iframe style="position:fixed;inset:0;width:100%;height:100%;border:0" src="https://tc.cclabs.app/fuzz/index.html?host=web"></iframe>'}));
  await page.route('https://tc.cclabs.app/**',async r=>{
   const path=new URL(r.request().url()).pathname;
   if(path.endsWith('index.html'))return r.fulfill({contentType:'text/html',body:'<html><head><meta name="viewport" content="width=device-width,initial-scale=1"></head><body style="margin:0"><div id="h" style="position:fixed;inset:0"></div></body></html>',headers:{'Content-Security-Policy':"default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob: https://images.scrolller.com; media-src 'self' blob: https://images.scrolller.com; frame-ancestors https://app.cclabs.app"}});
   let body;
   if(original&&/bambi-(sky|projection)\.js$/.test(path))body=execFileSync('git',['-c','filter.lfs.process=','-c','filter.lfs.smudge=','-c','filter.lfs.required=false','show',base+':ConditioningControlPanel/Resources/web'+path]);
   else body=readFileSync(root+'/..'+path);
   return r.fulfill({contentType:path.endsWith('.js')?'text/javascript':'application/octet-stream',body});
  });
  await page.route('https://images.scrolller.com/**',r=>r.fulfill({contentType:'image/svg+xml',body:'<svg xmlns="http://www.w3.org/2000/svg" width="320" height="224"><rect width="320" height="224" fill="#c5468f"/><circle cx="120" cy="110" r="65" fill="#ffdc45"/><path d="M160 200L220 25L300 200Z" fill="#158cb8"/></svg>'}));
  const cdp=await page.context().newCDPSession(page);await cdp.send('Emulation.setCPUThrottlingRate',{rate:throttle});
  await page.goto('https://app.cclabs.app/tc');
  const frame=page.frames().find(f=>f.url().includes('tc.cclabs.app'));
  const result=await frame.evaluate(async()=>{
   Math.random=()=>.5;
   const {createBambiProjection}=await import('/fuzz/ui/unlocks/themes-a/bambi-projection.js');
   const {createBambiSky}=await import('/fuzz/ui/unlocks/themes-a/bambi-sky.js');
   const host=document.getElementById('h'),handlers=new Map(),cleanups=[];
   let n=0;
   const pics={source:'scrolller',next:async opt=>opt.kind==='gif'?null:{url:'https://images.scrolller.com/test'+n+++'.svg',kind:'picture',credit:'test'},acquire:row=>({url:row.url,release(){}}),noteView(){}};
   const ctx={pictures:pics,on:(e,fn)=>{handlers.set(e,fn);return()=>{}},cleanup:fn=>cleanups.push(fn),frame:()=>()=>{}};
   const p=createBambiProjection(ctx,{creditHost:host});p.update(0);
   await new Promise(r=>setTimeout(r,100));p.update(.3);
   await new Promise(r=>setTimeout(r,100));p.update(.6);
   await new Promise(r=>setTimeout(r,100));p.update(.9);
   await new Promise(r=>setTimeout(r,100));p.update(1.2);
   const sky=createBambiSky(host,{projection:p});
   for(let i=0;i<4;i++)sky.update(.05);
   const tainted=canvas=>{try{canvas?.getContext('2d').getImageData(0,0,1,1);return false;}catch{return true;}};
   const result={mode:host.querySelector('.bambi-sky').dataset.skyRenderer,atlasTainted:tainted(p.canvas),overlayTainted:p.overlay?tainted(p.overlay):null};
   const samples=[];
   for(let i=0;i<120;i++){const t=performance.now();sky.update(.05);samples.push(performance.now()-t);}
   result.meanMs=samples.reduce((a,b)=>a+b,0)/samples.length;result.maxMs=Math.max(...samples);
   window.check={p,sky,ctx,handlers,cleanups};
   return result;
  });
  if(evidence)for(const [name,yaw,pitch] of [['front',.13,.19],['side',1.9,.35],['up',-1.1,1.2]]){
   await frame.evaluate(({yaw,pitch})=>{const {sky}=window.check;sky.camera.yaw=yaw;sky.camera.pitch=pitch;sky.update(.05);},{yaw,pitch});
   await page.screenshot({path:evidence+'/'+vw+'-'+name+'-'+(original?'before':'after')+'.png'});
  }
  const release=await frame.evaluate(()=>{const {p,sky,handlers}=window.check;handlers.get('pictures')();const overlayGone=!p.overlay;sky.dispose();p.dispose();return {overlayGone,remainingSky:document.querySelectorAll('.bambi-sky').length};});
  result.viewport=vw+"x"+vh;result.throttle=throttle;result.original=original;result.release=release;data.push(result);
  if(!original){assert.equal(result.mode,'webgl');assert.equal(result.atlasTainted,false);assert.equal(result.overlayTainted,true);assert.equal(release.overlayGone,true);assert.equal(release.remainingSky,0);}
  await page.close();
 }
 console.log(JSON.stringify(data,null,2));if(evidence)writeFileSync(evidence+'/bambi-results.json',JSON.stringify(data,null,2));
}finally{await browser.close();}
