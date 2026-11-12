// Run with the game server up. All photos and host replies are local fixtures.
import assert from 'node:assert/strict';
import {createRequire} from 'node:module';

const {chromium} = createRequire('C:/wt-fe/e2e/package.json')(process.env.PW_DIR || 'playwright');
const game = process.env.FUZZ_URL || 'http://127.0.0.1:8817/fuzz/index.html';
const browser = await chromium.launch({channel:process.env.BROWSER_CHANNEL || 'chrome',args:['--mute-audio']});
try {
  for (const width of [1280,390]) {
    const page = await browser.newPage({viewport:{width,height:850}});
    const errors=[];
    page.on('pageerror',e=>errors.push(e.message));
    // The loopback media host inserts this bridge. The drive needs no account.
    await page.route('**/__wheel-feed/bridge.js',r=>r.fulfill({contentType:'application/javascript',body:''}));
    await page.addInitScript(()=>{
      window.chrome ||= {};
      let receive;
      window.chrome.webview={
        addEventListener:(type,fn)=>{if(type==='message')receive=fn;},
        postMessage:frame=>{if(frame.type==='ready')queueMicrotask(()=>receive({data:{
          type:'init',protocol:1,caps:{scrolller:{consent:true,origin:'device'}},
        }}));},
      };
    });
    // The photo wall arrives with Trance Pictures since 2026-11-08 (Trance Theme brings three screens).
    await page.goto(game+'?fresh=1&buy=tr_feed&pictures=none');
    const wall=page.locator('.trance-photo-wall');
    await wall.waitFor({state:'attached'});
    await page.evaluate(async()=>{
      const p=fuzz.engine.pictures;
      window.galleryFiles=[];
      for (const [i,color] of ['red','blue','green','gold','purple','pink'].entries()) {
        const c=document.createElement('canvas');
        c.width=c.height=32;
        const g=c.getContext('2d');g.fillStyle=color;g.fillRect(0,0,32,32);
        const blob=await new Promise(r=>c.toBlob(r));
        galleryFiles.push(new File([blob],i+'.png',{type:'image/png',lastModified:100+i}));
      }
      p.setSource('own');
    });
    const count= n=>page.waitForFunction(n=>document.querySelectorAll('.trance-photo-wall .ta-side img').length===n,n);
    const urls=()=>wall.locator('img').evaluateAll(images=>images.map(i=>i.src));
    const rotate=()=>page.evaluate(()=>{for(let i=0;i<10;i++)fuzz.engine.step(1000);});
    await count(0);
    await page.evaluate(()=>fuzz.engine.pictures.addOwn([
      galleryFiles[0],galleryFiles[0],new File([galleryFiles[0]],'same-pixels.png',{type:'image/png'}),
      new File(['not a photo'],'broken.png',{type:'image/png'}),
    ]));
    await count(1);
    await page.evaluate(()=>fuzz.engine.pictures.addOwn(galleryFiles.slice(1,3)));
    await count(3);
    const three=await urls();
    await rotate();
    assert.deepEqual(await urls(),three);
    await page.evaluate(()=>fuzz.engine.pictures.addOwn(galleryFiles.slice(3)));
    await count(4);
    const identified=new Set();
    for (let i=0;i<6;i++) {
      const current=await urls();
      const keys=await wall.locator('img').evaluateAll(images=>images.map(img=>{
        const c=document.createElement('canvas');c.width=c.height=1;const g=c.getContext('2d');
        g.drawImage(img,0,0,1,1);return [...g.getImageData(0,0,1,1).data].join(',');
      }));
      assert.equal(new Set(keys).size,4);
      for(const key of keys)identified.add(key);
      await rotate();
      await page.waitForFunction(before=>[...document.querySelectorAll('.trance-photo-wall img')].some((img,i)=>img.src!==before[i]),current);
    }
    assert.equal(identified.size,6);
    await page.evaluate(()=>fuzz.engine.pictures.replaceOwn([galleryFiles[5]]));
    await count(1);
    const one=await urls();
    await rotate();assert.deepEqual(await urls(),one);
    await page.evaluate(()=>{const p=fuzz.engine.pictures;p.addOwn(galleryFiles);p.setSource('none');});
    await count(0);
    await rotate();assert.equal(await wall.locator('img').count(),0);
    const photoBytes=await page.evaluate(async()=>Promise.all(galleryFiles.map(async f=>[...new Uint8Array(await f.arrayBuffer())])));
    await page.route('**/gallery-fixture/*.png',route=>{
      const i=Number(new URL(route.request().url()).pathname.match(/(\d+)\.png$/)[1]);
      return route.fulfill({contentType:'image/png',body:Buffer.from(photoBytes[i%photoBytes.length])});
    });
    const feed=ids=>page.evaluate(ids=>{
      const p=fuzz.engine.pictures;
      p.setSource('scrolller');
      p.receiveScrolller({v:1,rows:ids.map(i=>({kind:'image',url:location.origin+'/gallery-fixture/'+i+'.png',mime:'image/png'}))});
    },ids);
    await feed([0,0,6]);await count(1);
    await feed([0,0,6,1,2,3]);await count(4);
    assert.deepEqual(await wall.locator('small').allTextContents(),Array(4).fill('via Scrolller'));
    await feed([1]);await count(1);
    await page.evaluate(()=>fuzz.engine.pictures.setRemoteConsent(false));
    await count(0);
    assert.deepEqual(errors,[]);
    await page.close();
    console.log(width+'px: local and Scrolller fixtures passed unique tiles, rotation, credit and source removal.');
  }
} finally { await browser.close(); }
