import test from 'node:test';
import assert from 'node:assert/strict';
import { createServer, request } from 'node:http';
import { spawn } from 'node:child_process';
import { fileURLToPath } from 'node:url';

test('local preview delegates consent and streams without exposing another origin', async () => {
  const visit='a'.repeat(48), asset='live-'+'b'.repeat(32);
  const received=[];
  let feedOrigin='';
  const feed=createServer((req,res)=>{
    received.push({path:req.url,origin:req.headers.origin,range:req.headers.range});
    if(req.url.startsWith('/__wheel-feed/state')) {
      res.setHeader('Content-Type','application/json');
      res.end(JSON.stringify({consent:true,rows:[{kind:'image',url:feedOrigin+'/__wheel-feed/'+asset+'?visit='+visit,mime:'image/webp'}]}));
    } else if(req.url.startsWith('/__wheel-feed/'+asset)) {
      res.writeHead(206,{'Content-Type':'image/webp','Content-Range':'bytes 0-3/4','Content-Length':4});res.end('test');
    } else { res.setHeader('Content-Type','application/json');res.end(JSON.stringify({consent:true,rows:[]})); }
  });
  await new Promise(resolve=>feed.listen(0,'127.0.0.1',resolve));
  feedOrigin='http://127.0.0.1:'+feed.address().port;
  const reservation=createServer();
  await new Promise(resolve=>reservation.listen(0,'127.0.0.1',resolve));
  const port=reservation.address().port;
  await new Promise(resolve=>reservation.close(resolve));
  const origin='http://127.0.0.1:'+port;
  const processHandle=spawn(process.execPath,[fileURLToPath(new URL('./tab-doc-preview-server.mjs',import.meta.url)),
    '--root',fileURLToPath(new URL('../../',import.meta.url)),'--port',String(port),'--feed',feedOrigin],{windowsHide:true,stdio:['ignore','pipe','pipe']});
  try {
    await new Promise((resolve,reject)=>{
      const timeout=setTimeout(()=>reject(new Error('Adapter did not start')),5000);
      processHandle.stdout.once('data',()=>{clearTimeout(timeout);resolve();});
      processHandle.once('error',reject);
      processHandle.once('exit',code=>{clearTimeout(timeout);reject(new Error('Adapter exited '+code));});
    });
    const page=await fetch(origin+'/fuzz/dev/tab-doc.html');
    assert.equal(page.status,200);assert.match(await page.text(),/Use Scrolller/);
    assert.equal(received.length,0,'opening the preview must not request an account or feed');
    const denied=await fetch(origin+'/__wheel-feed/on?visit='+visit,{method:'POST',headers:{Origin:'https://other.invalid'}});
    assert.equal(denied.status,403);assert.equal(received.length,0);
    const allowed=await fetch(origin+'/__wheel-feed/on?visit='+visit,{method:'POST',headers:{Origin:origin}});
    assert.equal(allowed.status,200);assert.equal(received.at(-1).origin,feedOrigin);
    const stateResponse=await fetch(origin+'/__wheel-feed/state?visit='+visit);
    assert.match(stateResponse.headers.get('cache-control'),/no-store/);
    const state=await stateResponse.json();
    assert.equal(new URL(state.rows[0].url).origin,origin);
    const image=await fetch(state.rows[0].url,{headers:{Range:'bytes=0-3'}});
    assert.equal(image.status,206);assert.equal(await image.text(),'test');
    assert.equal(received.at(-1).range,'bytes=0-3');
    assert.equal((await fetch(origin+'/__wheel-feed/state?visit=bad')).status,403);
    assert.equal((await fetch(origin+'/package.json')).status,404);
    const wrongHost=await new Promise(resolve=>{
      const req=request(origin+'/fuzz/dev/tab-doc.html',{headers:{Host:'other.invalid'}},res=>{res.resume();resolve(res.statusCode);});req.end();
    });
    assert.equal(wrongHost,403);
    await fetch(origin+'/__wheel-feed/off?visit='+visit,{method:'POST',headers:{Origin:origin}});
  } finally {
    processHandle.kill();
    feed.closeAllConnections();await new Promise(resolve=>feed.close(resolve));
  }
});
