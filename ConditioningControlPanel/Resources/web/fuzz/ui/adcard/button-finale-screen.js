/* The rear of the interface has no wall. A sparse, distant dust field gives
 * the fixed eye a frame of reference while the captured pieces float nearby. */
const cssUrl = new URL('./button-finale.css',import.meta.url).href;
export function loadFinaleStyle(doc) {
  if(doc.querySelector('link[data-button-finale]'))return;
  const link=doc.createElement('link');link.rel='stylesheet';link.href=cssUrl;link.dataset.buttonFinale='';doc.head.append(link);
}

export function captureButtonScreen(ctx) {
  const doc = ctx.world?.ownerDocument || document;
  loadFinaleStyle(doc);
  return {width:doc.defaultView.innerWidth,height:doc.defaultView.innerHeight, score:{value:Math.floor(Number(ctx.state?.fuzz) || 0)}};
}

export function createButtonSky(canvas) {
  const g = canvas.getContext('2d',{alpha:true}), doc = canvas.ownerDocument;
  let seed = 8731, W = 1, H = 1, px = 1, previous = null;
  const random = () => { seed = (seed * 1664525 + 1013904223) >>> 0; return seed / 4294967296; };
  const tau = Math.PI * 2;
  const stars = Array.from({length:680}, () => {
    const az = random()*tau, y = 2*random()-1, ring = Math.sqrt(1-y*y);
    return {x:Math.sin(az)*ring,y,z:Math.cos(az)*ring,r:.35+random()**4*1.9,a:.18+.74*random(),spark:random()>.95};
  });
  // A wide panorama of dusty, broken ribbons. It is curved by the moving
  // angular window and never has a hard edge or a surface for the eye to hit.
  const cloud = doc.createElement('canvas'); cloud.width = 2048; cloud.height = 800;
  const c = cloud.getContext('2d');
  if(c) {
    c.globalCompositeOperation='screen';
    const shades=['100,49,104','130,64,123','76,69,142','38,97,112','103,70,146'];
    for(let i=0;i<1100;i++) {
      const u=random(), k=random(), x=u*2048, a=u*tau;
      const h=360+Math.sin(a*2-.63)*110+Math.sin(a*5+.4)*23+Math.sin(a*9)*13;
      const y=h+(k-.5)*(90+70*Math.sin(a+.7));
      const r=4+random()**2*54, tone=shades[(Math.floor(u*8)+(random()>.74?2:0))%shades.length];
      for(const dx of x<r?[0,2048]:x>2048-r?[0,-2048]:[0]) {
        const color=c.createRadialGradient(x+dx,y,0,x+dx,y,r);
        color.addColorStop(0,`rgba(${tone},.09)`);color.addColorStop(.4,`rgba(${tone},.035)`);color.addColorStop(1,`rgba(${tone},0)`);
        c.fillStyle=color;c.fillRect(x+dx-r,y-r,r*2,r*2);
      }
    }
    for(let strand=0;strand<19;strand++) {
      c.strokeStyle=`rgba(193,126,181,${.008+random()*.025})`; c.lineWidth=.45+random()*1.8;c.beginPath();
      for(let j=0;j<=280;j++) {
        const u=j/280,a=u*tau,y=365+Math.sin(2*a-.63)*110+Math.sin(5*a+.4)*23+Math.sin(9*a)*13+
          5*Math.sin(a*3+strand*.23)+strand*.95;
        if(j)c.lineTo(2048*u,y);else c.moveTo(2048*u,y);
      }
      c.stroke();
    }
  }
  function resize(width,height) {
    previous=null;
    W=width;H=height;px=Math.min(1.12,1120/Math.max(width,height));
    canvas.width=Math.max(1,Math.ceil(W*px));canvas.height=Math.max(1,Math.ceil(H*px));
  }
  function paint(yaw,pitch,focal,time,calm) {
    if(!g)return;
    if(calm && previous && previous[0]===yaw && previous[1]===pitch && previous[2]===focal)return;
    previous=calm?[yaw,pitch,focal]:null;
    g.setTransform(px,0,0,px,0,0);g.clearRect(0,0,W,H);
    const cy=Math.cos(yaw),sy=Math.sin(yaw),cp=Math.cos(pitch),sp=Math.sin(pitch);
    const bandWidth=focal*tau, bandHeight=focal*2.3, xo=W/2-bandWidth*(.5+yaw/tau), yo=H*.52-bandHeight*.46+focal*pitch*.78;
    const wrap=((xo%bandWidth)+bandWidth)%bandWidth;
    g.globalAlpha=.98;
    for(let x=wrap-bandWidth;x<W;x+=bandWidth)g.drawImage(cloud,x,yo,bandWidth+1,bandHeight);
    for(const s of stars) {
      const side=s.x*cy-s.z*sy, forward=s.x*sy+s.z*cy;
      const up=s.y*cp-forward*sp, depth=forward*cp+s.y*sp;
      if(depth<=.12)continue;
      const x=W*.5+focal*side/depth,y=H*.5-focal*up/depth;
      if(x<-5||x>W+5||y<-5||y>H+5)continue;
      const r=s.r*Math.min(1.6,.72/depth);
      g.globalAlpha=s.a*(calm?1:(.88+.12*Math.sin(time*.45+s.x*20)));
      g.fillStyle=s.spark?'#fae3fc':'#c8d7ec';g.beginPath();g.arc(x,y,r,0,tau);g.fill();
      if(s.spark&&r>1) {g.fillRect(x-3*r,y-.44,r*6,.88);g.fillRect(x-.38,y-3*r,.76,r*6);}
    }
    g.globalAlpha=1;
  }
  return {resize,paint};
}
