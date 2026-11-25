// Loopback dev adapter. The existing private preview host retains all account and media policy.
// node tab-doc-preview-server.mjs --root C:/path/to/Resources/web --port 8898 --feed http://127.0.0.1:8817
import { createServer, request } from 'node:http';
import { createReadStream } from 'node:fs';
import { realpath, stat } from 'node:fs/promises';
import { extname, resolve, sep } from 'node:path';

const args = new Map();
for (let i=2;i<process.argv.length;i+=2) args.set(process.argv[i],process.argv[i+1]);
if (!args.get('--root')) throw new Error('--root must name the isolated Resources/web directory.');
const base = await realpath(resolve(args.get('--root')));
const port = Number(args.get('--port') || 8898);
if (!Number.isInteger(port) || port<1024 || port>65535) throw new Error('Invalid preview port.');
const origin = `http://127.0.0.1:${port}`;
const upstream = new URL(args.get('--feed') || 'http://127.0.0.1:8817');
if (upstream.protocol !== 'http:' || upstream.hostname !== '127.0.0.1' || !upstream.port ||
    upstream.username || upstream.password || upstream.pathname !== '/' || upstream.search || upstream.hash || upstream.origin === origin) {
  throw new Error('--feed must name a separate HTTP loopback preview port.');
}
const common = { 'Cache-Control':'no-store, private', 'X-Content-Type-Options':'nosniff',
  'Cross-Origin-Resource-Policy':'same-origin', 'Referrer-Policy':'strict-origin', 'X-Frame-Options':'SAMEORIGIN' };
const mime = { '.html':'text/html; charset=utf-8','.js':'text/javascript; charset=utf-8','.mjs':'text/javascript; charset=utf-8',
  '.css':'text/css; charset=utf-8','.json':'application/json','.png':'image/png','.jpg':'image/jpeg','.jpeg':'image/jpeg',
  '.webp':'image/webp','.avif':'image/avif','.gif':'image/gif','.svg':'image/svg+xml','.woff2':'font/woff2','.ttf':'font/ttf',
  '.mp3':'audio/mpeg','.wav':'audio/wav','.ogg':'audio/ogg','.mp4':'video/mp4','.webm':'video/webm','.glb':'model/gltf-binary','.wasm':'application/wasm' };
const visits = new Set();
const send = (res,status,type,body='') => { res.writeHead(status,{...common,'Content-Type':type});res.end(body); };
const deny = res => send(res,403,'text/plain','This preview is available only on its loopback origin.');

function proxy(req,res,url) {
  const bridge = url.pathname === '/__wheel-feed/bridge.js';
  const visit = url.searchParams.get('visit');
  const control = /^\/__wheel-feed\/(on|off|state)$/.test(url.pathname);
  const asset = /^\/__wheel-feed\/live-[a-f0-9]{32}$/.test(url.pathname);
  if ((!bridge && (!control && !asset || !/^[a-f0-9]{48}$/.test(visit||''))) ||
      (bridge && req.method !== 'GET') ||
      (!bridge && !['GET','HEAD','POST'].includes(req.method)) ||
      (req.method==='POST' && !/^\/__wheel-feed\/(on|off)$/.test(url.pathname))) return deny(res);
  const headers = { Host:upstream.host, 'Sec-Fetch-Site':'same-origin', Accept:req.headers.accept||'*/*' };
  if (req.method==='POST') headers.Origin=upstream.origin;
  if (req.headers.range) headers.Range=req.headers.range;
  const remote = request(new URL(url.pathname+url.search,upstream),{method:req.method,headers}, response=>{
    response.on('error',()=>res.destroy());
    if (control) {
      let bytes=0;const chunks=[];
      response.on('data',chunk=>{ bytes+=chunk.length; if(bytes>1048576) remote.destroy(); else chunks.push(chunk); });
      response.on('end',()=>{
        if(res.destroyed) return;
        try {
          const state=JSON.parse(Buffer.concat(chunks).toString('utf8'));
          if(url.pathname.endsWith('/on')&&state.consent===true) visits.add(visit);
          if(url.pathname.endsWith('/off')||state.consent===false) visits.delete(visit);
          if(Array.isArray(state.rows)) state.rows=state.rows.flatMap(row=>{
            try {
              const address=new URL(row.url);
              if(address.origin!==upstream.origin||!/^\/__wheel-feed\/live-[a-f0-9]{32}$/.test(address.pathname)) return [];
              return [{...row,url:origin+address.pathname+address.search}];
            } catch { return []; }
          });
          send(res,response.statusCode||502,'application/json',JSON.stringify(state));
        } catch { send(res,503,'application/json',JSON.stringify({consent:false,reason:'unavailable',rows:[]})); }
      });
    } else {
      const extra={};
      for(const name of ['content-type','content-length','content-range','accept-ranges']) if(response.headers[name]) extra[name]=response.headers[name];
      res.writeHead(response.statusCode||502,{...common,...extra});
      response.on('error',()=>res.destroy()); response.pipe(res);
    }
  });
  remote.setTimeout(45000,()=>remote.destroy());
  remote.on('error',()=>{ if(!res.headersSent) send(res,503,'application/json',JSON.stringify({consent:false,reason:'unavailable',rows:[]})); else res.destroy(); });
  res.on('close',()=>remote.destroy());
  remote.end();
}

const server=createServer(async(req,res)=>{
  if(req.headers.host!==new URL(origin).host||req.headers['sec-fetch-site']==='cross-site'||
     req.headers.origin&&req.headers.origin!==origin) return deny(res);
  try {
    const url=new URL(req.url,origin);
    if(url.pathname.startsWith('/__wheel-feed/')) {
      if(req.method==='POST'&&req.headers.origin!==origin) return deny(res);
      return proxy(req,res,url);
    }
    if(!['GET','HEAD'].includes(req.method)) return send(res,405,'text/plain');
    if(url.pathname==='/') {res.writeHead(302,{...common,Location:'/fuzz/dev/tab-doc.html?pics=scrolller'});res.end();return;}
    if(!/^\/(fuzz|vendor|glassroad|backroom|arcademy|dtrh)\//.test(url.pathname)) return send(res,404,'text/plain');
    const decoded=decodeURIComponent(url.pathname);
    if(decoded.split('/').some(part=>part.startsWith('.'))) return deny(res);
    const file=await realpath(resolve(base,'.'+decoded));
    const metadata=await stat(file);
    if(!file.startsWith(base+sep)||!metadata.isFile()) return deny(res);
    let start=0,end=metadata.size-1,status=200;
    const extra={'Content-Type':mime[extname(file).toLowerCase()]||'application/octet-stream','Accept-Ranges':'bytes'};
    if(req.headers.range) {
      const match=/^bytes=(\d+)-(\d*)$/.exec(req.headers.range);
      start=match?Number(match[1]):-1;end=match?.[2]?Math.min(end,Number(match[2])):end;
      if(!Number.isSafeInteger(start)||!Number.isSafeInteger(end)||start<0||start>end) {
        res.writeHead(416,{...common,'Content-Range':`bytes */${metadata.size}`});res.end();return;
      }
      status=206;extra['Content-Range']=`bytes ${start}-${end}/${metadata.size}`;
    }
    extra['Content-Length']=Math.max(0,end-start+1);
    res.writeHead(status,{...common,...extra});
    if(req.method==='HEAD'||!metadata.size) res.end();
    else createReadStream(file,{start,end}).on('error',()=>res.destroy()).pipe(res);
  } catch { if(!res.headersSent) send(res,404,'text/plain');else res.destroy(); }
});
function stop() {
  for(const visit of visits) {
    const req=request(new URL(`/__wheel-feed/off?visit=${visit}`,upstream),{method:'POST',headers:{Origin:upstream.origin,'Sec-Fetch-Site':'same-origin'}},res=>res.resume());
    req.on('error',()=>{});req.setTimeout(1500,()=>req.destroy());req.end();
  }
  visits.clear();server.close();
}
process.once('SIGINT',stop);process.once('SIGTERM',stop);
server.listen(port,'127.0.0.1',()=>console.log(`Museum preview: ${origin}/fuzz/dev/tab-doc.html?pics=scrolller`));
