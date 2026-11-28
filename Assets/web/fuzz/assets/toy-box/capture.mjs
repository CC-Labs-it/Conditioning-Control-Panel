// Offline thumbnails from the approved existing GLBs. Meshes and materials are not regenerated.
import { createServer } from 'node:http';
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { createRequire } from 'node:module';
import { fileURLToPath } from 'node:url';
import { join } from 'node:path';
const source = process.env.CHESS_WEB || 'C:/wt-fuzz/ConditioningControlPanel/Resources/web';
const out = fileURLToPath(new URL('.', import.meta.url));
const require = createRequire('C:/wt-fe/e2e/');
const { firefox } = require('playwright');
const html = `<style>body{margin:0;background:transparent}</style><script type="importmap">{"imports":{"three":"/vendor/three/three.module.min.js","three/addons/":"/vendor/three/addons/"}}</script><script type="module">
import * as T from 'three';import {GLTFLoader} from 'three/addons/loaders/GLTFLoader.js';
const r=new T.WebGLRenderer({alpha:true,antialias:true,preserveDrawingBuffer:true});r.setSize(180,240);r.setPixelRatio(1);r.setClearColor(0,0);r.outputColorSpace=T.SRGBColorSpace;r.toneMapping=T.ACESFilmicToneMapping;r.toneMappingExposure=1.3;document.body.append(r.domElement);
window.renderPiece=async(id)=>{const s=new T.Scene();s.add(new T.HemisphereLight(0xffffff,0x615568,2));const l=new T.DirectionalLight(0xfff4e5,3);l.position.set(-3,5,5);s.add(l);const fill=new T.DirectionalLight(0xc7d8ff,1.8);fill.position.set(3,3,-2);s.add(fill);const g=(await new GLTFLoader().loadAsync('/piecebypiece/assets/pieces/'+id+'.glb')).scene;s.add(g);g.rotation.y=-.35;g.updateMatrixWorld(true);const b=new T.Box3().setFromObject(g),size=b.getSize(new T.Vector3()),c=b.getCenter(new T.Vector3());const h=Math.max(size.y*1.2,size.x/0.75*1.2);const cam=new T.OrthographicCamera(-h*.375,h*.375,h*.5,-h*.5,.01,100);cam.position.set(c.x,c.y+size.y*.12,c.z+5);cam.lookAt(c);r.render(s,cam);const data=r.domElement.toDataURL('image/png');g.traverse(o=>{o.geometry?.dispose();if(o.material){for(const m of Array.isArray(o.material)?o.material:[o.material])m.dispose();}});return data;};window.ready=true;
</script>`;
const server=createServer(async(req,res)=>{try{const url=new URL(req.url,'http://localhost');if(url.pathname==='/'){res.setHeader('content-type','text/html');res.end(html);return;}if(!/^\/(vendor\/three\/|piecebypiece\/assets\/pieces\/)/.test(url.pathname)||url.pathname.includes('..')){res.writeHead(404);res.end();return;}res.setHeader('content-type',url.pathname.endsWith('.js')?'text/javascript':'model/gltf-binary');res.end(await readFile(join(source,url.pathname)));}catch(e){res.writeHead(404);res.end(String(e));}});
await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
let browser;
try{
  browser=await firefox.launch({headless:true,executablePath:'C:/Users/Admin/AppData/Local/ms-playwright/firefox-1538/firefox/firefox.exe',firefoxUserPrefs:{'media.volume_scale':'0.0'}});
  const page=await browser.newPage({viewport:{width:180,height:240}});page.on('pageerror',e=>console.error(e.message));
  await page.goto('http://127.0.0.1:'+server.address().port);await page.waitForFunction(()=>window.ready);
  await mkdir(out,{recursive:true});
  for(const id of ['pawn','rook','knight','bishop','queen','king']){const data=await page.evaluate(id=>window.renderPiece(id),id);await writeFile(join(out,id+'.png'),Buffer.from(data.split(',')[1],'base64'));console.log(id);}
}finally{await browser?.close();await new Promise(resolve=>server.close(resolve));}
