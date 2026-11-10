import { loadDomMedia } from '../arcade/slot-reel-media.js';
import { mediaReady, releasePlayer } from '../../media-cache.js';

const TAU = Math.PI * 2;
const MASK = 'radial-gradient(ellipse closest-side,#000 0% 44%,rgba(0,0,0,.86) 59%,rgba(0,0,0,.37) 81%,transparent 100%)';
const WORDS = ['BAMBI SLEEP', 'SINK', 'BIMBODOLL', 'DROP', 'BAMBI RESET', 'RELAX'];
const wrap = n => n - Math.floor(n);
const clamp = n => Math.max(0, Math.min(1, n));

function surface(doc, w, h) {
  const c = doc.createElement('canvas');
  c.width = w;
  c.height = h;
  return c;
}

// In an equirectangular chart the upper edge is one point on the sphere.
// Diminish all longitude variation before the pole. Every arc is periodic in u.
function atmosphere(g, w, h, t) {
  const wash = g.createLinearGradient(0, 0, 0, h);
  [[0,'#f5c9e3'],[.15,'#be97c3'],[.37,'#aa689e'],[.66,'#984f83'],[.84,'#d78da5'],[1,'#edb7c6']]
    .forEach(([at, color]) => wash.addColorStop(at, color));
  g.fillStyle = wash;
  g.fillRect(0,0,w,h);
  for (let k = 0; k < 4; k++) {
    const r = (.21 + k * .019) * w, cx = wrap(k * .25 + .018 * Math.sin(t*.15+k))*w;
    for (const shift of [-w,0,w]) {
      const cy = h * (.62 + .075*Math.sin(t*.1+k));
      const glow = g.createRadialGradient(cx+shift,cy,0,cx+shift,cy,r);
      glow.addColorStop(0,['#ffcab968','#d597ed78','#ff9cba70','#e8c9ff78'][k]);
      glow.addColorStop(1,'#f4c5e400');
      g.fillStyle=glow; g.fillRect(cx+shift-r,cy-r,2*r,2*r);
    }
  }
  // A few thin nacre ribbons remain visible between the projected exposures.
  for (let k=0;k<6;k++) {
    g.beginPath();
    for(let i=0;i<=128;i++) {
      const a=i/128*TAU, y=.62+.15*Math.sin(a*2+.4*Math.sin(t*.17)+k*.18)+.08*Math.sin(a*3-t*.09+k*.26);
      if(i)g.lineTo(i*w/128,y*h);else g.moveTo(0,y*h);
    }
    g.lineWidth=h*(k%2?.008:.0025);
    g.strokeStyle=k%2?'rgba(255,226,218,.085)':'rgba(255,233,226,.26)';
    g.stroke();
  }
}

function soapFilm(g,w,h,t) {
  // These distant overlapping soap-film contours are part of the sky itself.
  // A player who left pictures off gets a complete, softly moving environment.
  for(let i=0;i<13;i++) {
    const u=wrap(i*.381966+.014*Math.sin(t*.13+i)),v=.18+.46*((i*29%17)/17);
    const radius=h*(i%4===0?.23:.065+(i*7%10)*.010);
    for(const turn of [-1,0,1]) {
      const cx=(u+turn)*w,cy=h*(1-v)+h*.014*Math.sin(i+.16*t);
      if(cx+radius<0||cx-radius>w)continue;
      g.save();g.translate(cx,cy);g.scale(.5/Math.cos(v*Math.PI/2),1);
      const film=g.createRadialGradient(0,0,0,0,0,radius);
      [[0,'#b49ec306'],[.67,'#dcbada0a'],[.84,'#ebc18b20'],[.91,'#8bcbd55c'],
        [.955,'#fff1dbb3'],[.976,'#e998d566'],[1,'#f6cbea00']].forEach(([p,c])=>film.addColorStop(p,c));
      g.fillStyle=film;g.fillRect(-radius,-radius,2*radius,2*radius);
      g.strokeStyle='#fff1e1a8';g.lineWidth=radius*.018;
      g.beginPath();g.arc(0,0,radius*.905,-2.5,-1.63);g.stroke();g.restore();
    }
  }
}

// Eighteen overlapping media exposures, six word exposures. Their centers
// follow staggered latitudes while their broad feathered edges meet seamlessly.
const regions = Array.from({length: 18}, (_,i) => {
  const band=Math.floor(i/6), lane=i%6;
  return {u:lane/6+[.015,.079,.042][band],v:[.20,.46,.72][band]+.04*Math.sin(i*2.1),
    spanU:[.235,.255,.285][band],spanV:[.48,.52,.49][band]};
});
const wordSites = [[.008,.42],[.160,.27],[.291,.55],[.505,.29],[.738,.54],[.868,.30]];

function drawWords(g,w,h,t) {
  g.textAlign='center';g.textBaseline='middle';g.lineJoin='round';
  for (let i=0;i<WORDS.length;i++) {
    const [u,v]=wordSites[i], text=WORDS[i];
    const size=h*(text.length<6?.071:.049);
    const y=h*(1-v+.0025*Math.sin(t*.19+i));
    g.save();
    g.font='italic 700 '+size+'px Georgia,serif';
    // Longitude spans 360 degrees, latitude only 90. Undo the chart's
    // horizontal stretch here so the letters read normally from inside.
    const xScale=.50/Math.cos(v*Math.PI/2);
    for(const shift of [-w,0,w]) {
      const x=u*w+shift;
      if (x < -w*.15 || x>w*1.15) continue;
      g.save();g.translate(x,y);g.scale(xScale,1);
      g.shadowColor='rgba(92,39,79,.66)';g.shadowBlur=h*.006;
      g.strokeStyle='rgba(107,52,89,.36)';g.lineWidth=size*.029;g.strokeText(text,0,0);
      g.fillStyle='rgba(255,231,234,.80)';g.fillText(text,0,0);g.restore();
    }
    g.restore();
  }
}

function footprint(site,i,t) {
  return {...site,u:site.u+.011*Math.sin(t*.20+i),v:site.v+.018*Math.sin(t*.14+i*2)};
}

function framePatch(art, patch, mask, latitude) {
  const p=patch.getContext('2d'), pw=patch.width, ph=patch.height;
  const src=art.decoded?.canvas || art.element;
  if (!src) return false;
  const sw=src.videoWidth || src.naturalWidth || src.width, sh=src.videoHeight || src.naturalHeight || src.height;
  if (!(sw&&sh)) return false;
  p.clearRect(0,0,pw,ph);
  // Fit the full photograph or clip into its feathered field, including
  // portraits. Longitude and latitude have different physical scales.
  const ratio=(.255*2/.52)*2*Math.cos(latitude*Math.PI/2);
  const dw=pw*Math.min(1,sw/sh/ratio),dh=ph*Math.min(1,ratio*sh/sw);
  const x=(pw-dw)/2,y=(ph-dh)/2;
  try { p.drawImage(src,x,y,dw,dh); }
  catch { return false; }
  p.globalCompositeOperation='destination-in';p.drawImage(mask,x,y,dw,dh);p.globalCompositeOperation='source-over';
  return true;
}

function paintRegion(ctx,patch,motion) {
  const w=ctx.canvas.width,h=ctx.canvas.height;
  const dw=motion.spanU*w,dh=motion.spanV*h;
  const x=motion.u*w-dw/2,y=(1-motion.v)*h-dh/2;
  for(const shift of [-w,0,w]) if(x+shift<w && x+shift+dw>0) ctx.drawImage(patch,x+shift,y,dw,dh);
}

// Warp a CSS rectangle onto the projected quadrilateral, including perspective.
// Neighboring pieces share corner positions and the same naturally animated GIF.
function quadCss(p,w,h) {
  const [a,b,c,d]=p, dx1=b.x-c.x,dx2=d.x-c.x,dy1=b.y-c.y,dy2=d.y-c.y;
  const dx=a.x-b.x+c.x-d.x,dy=a.y-b.y+c.y-d.y,det=dx1*dy2-dx2*dy1;
  if(Math.abs(det)<.001) return null;
  const k=(dx*dy2-dx2*dy)/det,l=(dx1*dy-dx*dy1)/det;
  return 'matrix3d('+[(b.x-a.x+k*b.x)/w,(b.y-a.y+k*b.y)/w,0,k/w,
    (d.x-a.x+l*d.x)/h,(d.y-a.y+l*d.y)/h,0,l/h,0,0,1,0,a.x,a.y,0,1].join(',')+')';
}

function createNativePatch(doc,host,art) {
  const pieces=[], cols=5, rows=3, tw=80,th=84, w=tw*cols,h=th*rows;
  for(let j=0;j<rows;j++)for(let i=0;i<cols;i++) {
    const cell=doc.createElement('div');
    cell.style.cssText='position:absolute;left:0;top:0;overflow:hidden;transform-origin:0 0;pointer-events:none;width:'+(tw+.25)+'px;height:'+(th+.25)+'px';
    const img=art.element.cloneNode(false);
    img.style.cssText='position:absolute;object-fit:cover;pointer-events:none;width:'+w+'px;height:'+h+'px;left:'+(-i*tw)+'px;top:'+(-j*th)+'px;mask-image:'+MASK;
    cell.appendChild(img);host.appendChild(cell);pieces.push({cell,i,j});
  }
  return {art,remove(){for(const p of pieces)p.cell.remove();},place(sky,site,opacity) {
    const p=(i,j)=>sky.project(wrap(site.u+site.spanU*(i/cols-.5)),site.v+site.spanV*(.5-j/rows));
    for(const {cell,i,j} of pieces) {
      const pts=[p(i,j),p(i+1,j),p(i+1,j+1),p(i,j+1)];
      const matrix=pts.every(q=>q && q.depth>.01) && quadCss(pts,tw,th);
      cell.hidden=!matrix;
      if(matrix) {cell.style.transform=matrix;cell.style.opacity=opacity;}
    }
  }};
}

// The pictures cache can hand over a player whose first frame is already
// decoded. Both prepared and newly allocated players use its lease lifecycle.
async function borrowPlayer(pics,row,doc,signal) {
  const el=pics.takePlayer(row,doc);
  const event=row.kind==='clip'?'loadeddata':'load';
  const ready=mediaReady(el) || await new Promise(resolve=>{
    const finish=()=>{
      clearTimeout(timer);
      el.removeEventListener(event,finish);
      el.removeEventListener('error',finish);
      signal.removeEventListener('abort',finish);
      resolve(!signal.aborted && mediaReady(el));
    };
    const timer=setTimeout(finish,9000);
    el.addEventListener(event,finish);el.addEventListener('error',finish);
    signal.addEventListener('abort',finish,{once:true});
    if(signal.aborted || mediaReady(el) || el.error)finish();
  });
  if(!ready || signal.aborted){releasePlayer(el);return null;}
  return {element:el,dispose:()=>releasePlayer(el)};
}

export function prewarmBambiProjection(ctx) {
  const layer=ctx.layer('ending'), holder=layer.ownerDocument.createElement('div');
  holder.setAttribute('aria-hidden','true');
  holder.style.cssText='position:absolute;width:1px;height:1px;overflow:hidden;opacity:0;pointer-events:none';
  layer.appendChild(holder);
  return createBambiProjection(ctx,{creditHost:holder,warmHost:holder});
}

export function createBambiProjection(ctx,{creditHost,demoUrls=[],warmHost=null}) {
  const doc=creditHost.ownerDocument, pics=ctx.pictures;
  const W=doc.documentElement.clientWidth<700?1024:1536,H=W/2;
  let canvas, g, serial=0, displayOnly=false, active=true, ended=false, currentTime=0, lastPaint=-Infinity;
  let warming=!!warmHost, stopWarm=null;
  const mask=surface(doc,320,224), mg=mask.getContext('2d');
  mg.scale(mask.width/2,mask.height/2);
  const feather=mg.createRadialGradient(1,1,0,1,1,1);
  [[0,1],[.44,1],[.59,.86],[.81,.37],[1,0]].forEach(([r,a])=>feather.addColorStop(r,'rgba(255,255,255,'+a+')'));
  mg.fillStyle=feather;mg.fillRect(0,0,2,2);
  const channels=Array.from({length:4},()=>({art:null,old:null,due:0,loading:null}));
  const native=new Map();
  const credit=doc.createElement('small');
  credit.textContent='via Scrolller';
  credit.style.cssText='position:absolute;left:12px;bottom:12px;z-index:6;color:white;font:11px system-ui;text-shadow:0 1px 4px black;pointer-events:none';
  credit.hidden=true;creditHost.appendChild(credit);
  let demoIndex=0, nextLoad=0, program=0, nextCut=2;
  const arrangement=regions.map((_,i)=>({channel:(i+Math.floor(i/6))%4,old:0,cut:-Infinity}));
  const visible=()=>active&&!ended&&!ctx.paused&&!doc.hidden;
  function resetCanvas() {
    // Assigning a fresh backing store drops tainted CDN pixels on revocation.
    canvas=surface(doc,W,H);g=canvas.getContext('2d',{alpha:false});
    displayOnly=false;lastPaint=-Infinity;
    atmosphere(g,W,H,currentTime);soapFilm(g,W,H,currentTime);drawWords(g,W,H,currentTime);serial++;
  }
  function discard(art) {art?.dispose();art?.release();}
  function clear() {
    for(const c of channels) {
      c.loading?.abort();c.loading=null;discard(c.art);discard(c.old);c.art=c.old=null;c.due=0;
    }
    for(const layer of native.values())layer.remove();
    native.clear();credit.hidden=true;nextLoad=0;resetCanvas();
  }
  function sync() {
    if(!visible() || ctx.gentle || ctx.reduced) {
      for(const layer of native.values())layer.remove();
      native.clear();
    }
    for(const c of channels) for(const art of [c.old,c.art]) if(art?.kind==='clip') {
      if(warming||!visible()||ctx.gentle||ctx.reduced) art.element.pause();
      else if(art.element.paused && performance.now()>art.playAfter) {
        art.playAfter=performance.now()+1000;
        art.element.play().catch(()=>{});
      }
    }
    if(!visible()) for(const c of channels) {
      if(c.loading){c.loading.abort();c.due=0;}
    }
  }
  async function tune(c,now) {
    c.due=now+1.5;
    const job=new AbortController();c.loading=job;
    let lease,loaded,decoded;
    try {
      let row;
      if(demoUrls.length) row={url:demoUrls[demoIndex++%demoUrls.length],kind:'clip'};
      else if(pics && ['own','scrolller'].includes(pics.source)) {
        const opt={allowScrolller:true,niche:'bimbo',previousUrl:c.art?.url};
        row=await pics.next({...opt,kind:'gif'});
        if(!row&&!job.signal.aborted)row=await pics.next({...opt,kind:'picture'});
      }
      if(!row || job.signal.aborted)return;
      if(channels.some(c=>c.art?.url===row.url || c.loading?.url===row.url)) { c.due=now+3.5;return; }
      // Keep a still as a fallback when the live feed sends photos before its first clips.
      // Leave the other warm slots open for motion as the catalog grows.
      if (warming && pics?.source === 'scrolller' && row.kind === 'picture' &&
          channels.some(slot => slot.art?.kind === 'picture' || slot.loading?.kind === 'picture')) return;
      job.url=row.url;
      job.kind=row.kind;
      // A cache player holds its own lease. Animated local files need a byte
      // lease for the frame decoder instead; native web GIFs stay display-only.
      const canBorrow=!demoUrls.length && typeof pics.takePlayer==='function' && row.kind!=='gif';
      lease=demoUrls.length||canBorrow?{url:row.url,release(){}}:pics.acquire(row);
      const u=new URL(lease.url,doc.baseURI);
      const remote=u.protocol!=='blob:' && u.protocol!=='data:' && u.origin!==new URL(doc.baseURI).origin;
      if(row.kind==='gif'&&!remote) {
        const {decodedSource}=await import('../../../../backroom/room/gif-decode.js');
        if(!job.signal.aborted) decoded=await decodedSource(lease.url,{maxEdge:448,maxFps:18,signal:job.signal});
      }
      if(!decoded&&!job.signal.aborted)loaded=canBorrow
        ? await borrowPlayer(pics,row,doc,job.signal)
        : await loadDomMedia({...row,url:lease.url},job.signal,doc);
      if(job.signal.aborted || !(decoded||loaded))return;
      const src=loaded || decoded, art={...row,decoded,element:loaded?.element,remote,
        native:row.kind==='gif'&&remote,playAfter:0,release:lease.release,dispose:()=>src.dispose()};
      lease=loaded=decoded=null;
      discard(c.old);c.old=c.art;c.art=art;c.changed=currentTime;c.due=currentTime+6+Math.random()*2;
      if(art.element?.tagName==='VIDEO') {
        // A mounted player keeps its inline playback lifecycle on mobile.
        art.element.style.cssText='position:absolute;bottom:0;left:0;width:1px;height:1px;opacity:0;pointer-events:none';
        creditHost.appendChild(art.element);
        art.dispose=()=>{src.dispose();art.element.remove();};
      }
      sync();
    } catch { /* A rejected file keeps the previous exposure in place. */ }
    finally {loaded?.dispose();decoded?.dispose();lease?.release();if(c.loading===job)c.loading=null;}
  }
  function exposures(t) {
    const usable=channels.filter(c=>c.art);
    return usable.length?regions.flatMap((r,i)=>{
      const tv=arrangement[i],c=usable[tv.channel%usable.length],blend=clamp((t-c.changed)/1.3);
      const swap=clamp((t-tv.cut)/1.6),from=usable[tv.old%usable.length];
      const site=footprint(r,i,t);
      return [swap<1&&from!==c&&{art:from.art,site,opacity:.96*(1-swap),key:i+'before'},
        c.old&&{art:c.old,site,opacity:.96*(1-blend)*swap,key:i+'old'},
        {art:c.art,site,opacity:.96*(c.old?blend:1)*(from===c?1:swap),key:i+'live'}].filter(e=>e&&e.opacity>.01);
    }):[];
  }
  function schedule(t) {
    if(t>=nextLoad && channels.filter(c=>c.loading).length<2) {
      const c=channels.filter(c=>!c.loading&&c.due<=t&&(!warming||!c.art)).sort((a,b)=>a.due-b.due)[0];
      if(c) {nextLoad=t+(c.art ? .9 : .2);void tune(c,t);}
    }
  }
  function update(t) {
    if(!visible() || !Number.isFinite(t))return;
    currentTime=t;
    schedule(t);
    // During the absorption only deal and decode the first set. Keep these
    // exact players paused and ready until the first frame inside the bubble.
    if(warming)return;
    sync();
    if(t-lastPaint<(ctx.gentle||ctx.reduced?1/6:1/18) && t>=lastPaint)return;
    lastPaint=t;
    for(const c of channels) {
      if(c.old&&t-c.changed>1.3){discard(c.old);c.old=null;}
      for(const art of [c.old,c.art])art?.decoded?.tick(performance.now(),ctx.gentle||ctx.reduced);
    }
    // A small library still changes independently around the viewer.
    if(t>=nextCut) {
      const region=arrangement[(program++*7)%arrangement.length];
      region.old=region.channel;region.channel++;region.cut=t;nextCut=t+1.75;
    }
    const shots=exposures(t);
    atmosphere(g,W,H,t);
    if(!shots.length)soapFilm(g,W,H,t);
    const painted=new Set();
    for(const {art,site,opacity} of shots) {
      if(art.native && !ctx.gentle && !ctx.reduced) continue;
      if(art.remote)displayOnly=true;
      // A decoder is sampled once per atlas pass. Every repeat uses the small
      // feathered frame, including the copies on the other side of the seam.
      if(!painted.has(art)) {
        art.patch ||= surface(doc,mask.width,mask.height);
        if(!framePatch(art,art.patch,mask,.46))continue;
        painted.add(art);
      }
      g.globalAlpha=opacity;paintRegion(g,art.patch,site);
    }
    g.globalAlpha=1;
    // Common rim and cap marry the offset exposures into one continuous dome.
    const cap=g.createLinearGradient(0,0,0,H);
    [[0,'#f5c9e3'],[.06,'#eabbdacc'],[.21,'#d99ec000'],[.84,'#d99ec000'],[1,'#edb7c6dc']].forEach(([p,c])=>cap.addColorStop(p,c));
    g.fillStyle=cap;g.fillRect(0,0,W,H);
    drawWords(g,W,H,t);credit.hidden=!shots.some(s=>s.art.credit);serial++;
    for(const {art} of shots) if(art.credit && !art.viewed) {art.viewed=true;pics?.noteView?.();}
  }
  function updateNativeGifs(sky) {
    if(!sky?.nativeMediaHost || !sky.project)return;
    const taken=new Set();
    if(visible() && !ctx.gentle && !ctx.reduced) for(const {art,key,site,opacity} of exposures(currentTime)) {
      if(!art.native)continue;
      // GIF players are only needed on the visible arc. The web feed's video
      // path and every device-local animation bypass this overlay completely.
      const points=[-.45,0,.45];
      const inView=points.some(x=>points.some(y=>sky.project(wrap(site.u+x*site.spanU),clamp(site.v+y*site.spanV))?.visible));
      if(!inView)continue;
      taken.add(key);
      if(native.get(key)?.art!==art) {native.get(key)?.remove();native.set(key,createNativePatch(doc,sky.nativeMediaHost,art));}
      native.get(key).place(sky,site,opacity);
    }
    // Projecting light onto the sky keeps pale type visible through a GIF.
    sky.nativeMediaHost.style.mixBlendMode=taken.size?'lighten':'normal';
    for(const [key,layer] of native)if(!taken.has(key)){layer.remove();native.delete(key);}
  }
  resetCanvas();
  const offs=[ctx.on('pictures',clear),...['pause','suspend','resume'].map(ev=>ctx.on(ev,sync))];
  doc.addEventListener('visibilitychange',sync);
  if(warming) {
    stopWarm=ctx.frame(dt=>update(currentTime+dt));
    update(0);
  }
  function present(stage) {
    if(ended || !warming)return;
    stopWarm?.();stopWarm=null;warming=false;
    creditHost=stage;stage.appendChild(credit);
    for(const c of channels) {
      if(c.art?.element?.tagName==='VIDEO')stage.appendChild(c.art.element);
      c.changed=0;c.due=c.art?6+Math.random()*2:0;
    }
    warmHost.remove();warmHost=null;
    currentTime=0;lastPaint=-Infinity;nextLoad=0;
    // createBambiSky draws immediately: build the actual selected images now
    // so that first render samples the prepared projection, without a splash.
    update(0);
  }
  function dispose() {
    if(ended)return;
    ended=true;
    for(const off of offs)off?.();
    doc.removeEventListener('visibilitychange',sync);
    stopWarm?.();stopWarm=null;
    clear();credit.remove();warmHost?.remove();canvas.width=canvas.height=1;
  }
  ctx.cleanup(dispose);
  return {get canvas(){return canvas;},get revision(){return serial;},get displayOnly(){return displayOnly;},
    get readyForPop() {
      if (pics?.source === 'scrolller') return channels.some(c => c.art && c.art.kind !== 'picture');
      if (pics?.source === 'own' && pics.ownCount) return channels.some(c => c.art);
      return true;
    },
    present,update,updateNativeGifs,setActive(on){active=!!on;sync();},dispose};
}
