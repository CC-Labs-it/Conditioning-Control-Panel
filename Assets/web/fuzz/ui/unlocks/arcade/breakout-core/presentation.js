import { slotEffects } from '../slot-effects.js';
import { WIDTH, HEIGHT } from './game.js';

const powerColor = {fireball:'#ffaf53', shield:'#73e8ed'};

// Reuse the station's flame and shield silhouettes at the cabinet's pixel scale.
function emblem(g, kind, x, y, size) {
  g.save(); g.translate(x,y); g.scale(size,size); g.beginPath();
  if (kind === 'shield') {
    g.moveTo(-.82,-.7); g.lineTo(0,-1); g.lineTo(.82,-.7); g.lineTo(.7,.3);
    g.quadraticCurveTo(.45,.75,0,1); g.quadraticCurveTo(-.65,.62,-.72,.25);
  } else {
    g.moveTo(.18,-1); g.bezierCurveTo(.18,-.46,1,-.16,.68,.56);
    g.bezierCurveTo(.18,1.5,-1.16,.66,-.66,-.08); g.lineTo(-.34,.12);
    g.quadraticCurveTo(-.32,-.52,.18,-1);
  }
  g.closePath(); g.fillStyle=powerColor[kind]; g.fill();
  g.strokeStyle='#402b46'; g.lineWidth=.23; g.stroke(); g.restore();
}

export function drawSpecialBrick(g, b) {
  if (b.drop) {
    const cx = b.x + b.w / 2, cy = b.y + b.h / 2;
    g.fillStyle='#35263b'; g.beginPath(); g.roundRect(cx-7,cy-5.5,14,11,4); g.fill();
    emblem(g,b.drop,cx,cy,4.5);
  } else if (b.armor > 1) {
    g.strokeStyle='#453950'; g.lineWidth=1.8;
    for (const end of [b.x+2,b.x+b.w-5]) {
      g.strokeRect(end,b.y+1,3,b.h-2);
      g.fillStyle='#f8ede1'; g.fillRect(end+1,b.y+3,1.5,1.5);
    }
    if (b.hp === 1) {
      const mid=b.x+b.w/2;
      g.beginPath(); g.moveTo(mid,b.y); g.lineTo(mid-2,b.y+4);
      g.lineTo(mid+2,b.y+7); g.lineTo(mid-1,b.y+b.h); g.stroke();
    }
  }
}

export function drawDrop(g, d) {
  g.fillStyle='#241c32'; g.strokeStyle=powerColor[d.kind]; g.lineWidth=1.2;
  g.beginPath(); g.roundRect(d.x-8,d.y-6,16,12,5); g.fill(); g.stroke();
  emblem(g,d.kind,d.x,d.y,4.6);
}

export function createBrickPhoto(ctx, canvas, credit) {
  const doc = canvas.ownerDocument, effects = slotEffects(ctx);
  let pending = null, photo = null, board = -1, revision = 0;
  function resetPhoto() {
    revision++;
    pending?.removeAttribute('src'); pending = null; photo = null; board = -1;
    credit.hidden = true;
  }
  function clear() { resetPhoto(); effects.clear(); }
  async function prepare(index) {
    if (board === index) return;
    resetPhoto(); board = index;
    if (!['own','scrolller'].includes(ctx.pictures.source)) return;
    const rows = ctx.pictures.stillsForDisplay(64,{allowScrolller:true});
    if (!rows.length) return;
    const row = rows[index % rows.length];
    const version=revision;
    const lease=ctx.pictures.acquire(row);
    let image=null;
    try {
      if (row.kind !== 'picture' || version !== revision) return;
      image=new doc.defaultView.Image();
      pending=image; image.referrerPolicy='strict-origin'; image.src=lease.url;
      await image.decode();
      if (pending !== image) return;
      const still = doc.createElement('canvas');
      const scale=Math.min(1, 1024 / Math.max(image.naturalWidth,image.naturalHeight));
      still.width=Math.max(1,Math.round(image.naturalWidth*scale)); still.height=Math.max(1,Math.round(image.naturalHeight*scale));
      still.getContext('2d').drawImage(image,0,0,still.width,still.height);
      photo={url:row.url,image:still,credit:row.credit};
    } catch { /* A missing still leaves the ordinary colored brick playable. */ }
    finally { if (pending === image) pending=null; lease.release(); }
  }
  function draw(g, b) {
    if (!photo) return;
    const {image} = photo, scale=Math.max((b.w-2)/image.width,(b.h-2)/image.height);
    const sw=(b.w-2)/scale, sh=(b.h-2)/scale;
    g.drawImage(image,(image.width-sw)/2,(image.height-sh)/2,sw,sh,b.x+1,b.y+1,b.w-2,b.h-2);
    g.strokeStyle='#ffebcf'; g.lineWidth=1; g.strokeRect(b.x+.5,b.y+.5,b.w-1,b.h-1);
    if (photo.credit) { credit.textContent=photo.credit; credit.hidden=false; }
  }
  function hit(e) {
    credit.hidden=true;
    if (!photo || ctx.gentle || ctx.reduced) return;
    const r=canvas.getBoundingClientRect(), w=34*r.width/WIDTH, h=13*r.height/HEIGHT;
    effects.start('picture',{portrait:()=>({x:r.left+e.x*r.width/WIDTH,y:r.top+e.y*r.height/HEIGHT,w,h})},{preferred:photo});
  }
  ctx.on('pictures',clear);
  ctx.on('pictures-added',()=>{ if (!photo && !pending) board=-1; });
  ctx.cleanup(clear);
  return {prepare, draw, hit, effects};
}

const folder='assets/audio/breakout/';
const music={source:'breakout_music',sourceLabel:'Breakout music'};
const hits={source:'mini_breakout',sourceLabel:'Breakout effects'};
const levels={brick:.51, damage:.34, wall:.16, paddle:.36, drop:.26, shield:.58, fireball:.58, save:.48, clear:.62};

export function breakoutAudio(ctx) {
  let loop = null, controller = null;
  ctx.audio.mixer?.register(hits.source,hits.sourceLabel);
  ctx.audio.mixer?.register(music.source,music.sourceLabel);
  function audible(on) {
    if (on && !loop) {
      controller = new AbortController();
      loop = ctx.audio.loop(folder+'music.ogg',{bus:'ambient',gain:.54,fadeMs:350,...music});
    } else if (!on && loop) {
      controller.abort(); loop.stop(0); loop=null; controller=null;
    }
  }
  function cue(e) {
    if (!loop) return;
    const name = e.type === 'catch' ? e.kind : e.type;
    if (!Object.hasOwn(levels,name)) return;
    const step = e.type === 'brick' ? [0,2,4,7][e.row % 4] : 0;
    const pan = Number.isFinite(e.x) ? (e.x / WIDTH - .5) * 1.3 : 0;
    ctx.audio.play(folder+name+'.ogg',{bus:'sfx',gain:levels[name],rate:2**(step/12),pan,signal:controller.signal,...hits});
  }
  ctx.cleanup(()=>audible(false));
  return {audible,cue};
}
