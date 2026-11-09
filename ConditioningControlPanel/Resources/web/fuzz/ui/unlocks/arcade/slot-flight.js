import { POP_PICTURE_OPACITY } from '../../pop-picture.js';

// Rects are CSS client pixels until converted to the game viewport's backing coordinates.
export function clientRectToViewport(rect, viewport, width, height) {
  return {x:(rect.x-viewport.left)*width/viewport.width,y:(rect.y-viewport.top)*height/viewport.height,
    w:rect.w*width/viewport.width,h:rect.h*height/viewport.height};
}

const clamp = n => Math.max(0, Math.min(1,n));
const mix = (a,b,t) => a+(b-a)*t;
export const PRINT_DELAY = 300;
export const PRINT_LIFE = 3400;

// These are fractions of the available space around a fitted print. The early
// throws fan out, leaving their paper edges visible as the next image arrives.
const landings = [
  {x:-.86,y:.52, tilt:-8, size:.88},
  {x:.87,y:-.72, tilt:7, size:.92},
  {x:.03,y:.69, tilt:-2.5, size:.89},
];
let deal = 0;
export function dealPrintPoses() {
  const turn=deal++;
  const mirror=turn%2 ? -1 : 1;
  return landings.map((_,i)=>{
    const p=landings[(i+turn)%landings.length];
    return {x:mirror*p.x, y:p.y, tilt:mirror*(p.tilt+Math.random()*1.8-.9), size:p.size,
      swing:mirror*(i%2 ? -1 : 1)};
  });
}

export function flashFlight(origin,width,height,aspect,age,reduced=false,pose=landings[0]) {
  // The photograph keeps its aspect. A wider bottom border makes room for credit.
  const ratio=Math.max(.55,Math.min(2,Number.isFinite(aspect)&&aspect>0?aspect:1));
  const edge=Math.min(1,ratio);
  const paperRatio=(ratio+.044*edge)/(1+.10*edge);
  const angle=pose.tilt*Math.PI/180;
  const s=Math.abs(Math.sin(angle)), c=Math.cos(angle);
  const h=pose.size*Math.min(width/(paperRatio*c+s),height/(paperRatio*s+c));
  const w=paperRatio*h;
  const dx=Math.min(width*.14,(width-w*c-h*s)/2)*pose.x;
  const dy=Math.min(height*.12,(height-w*s-h*c)/2)*pose.y;
  const p=clamp(age/700), t=reduced?1:p*p*(3-2*p);
  const arc=reduced?0:4*p*(1-p);
  const settle=Math.max(0,age-700)/1000;
  const wobble=reduced?0:Math.sin(settle*15)*Math.exp(-settle*9)*.017;
  const fw=mix(origin.w,w,t), fh=mix(origin.h,h,t);
  const swing=pose.swing??1;
  const x=mix(origin.x+origin.w/2,width/2+dx,t) + arc*swing*Math.min(34,width*.035);
  const y=mix(origin.y+origin.h/2,height/2+dy,t) - arc*Math.min(50,height*.07);
  return {x:x-fw/2,y:y-fh/2,w:fw,h:fh,grow:t,angle:angle+(1-t)*swing*.14+wobble*swing,
    opacity:clamp(age/(reduced?500:110))*clamp((PRINT_LIFE-age)/900)};
}

export function drawPhotoPrint(g,frame,photo) {
  const image=photo.image, iw=image.naturalWidth||image.width, ih=image.naturalHeight||image.height;
  if(!iw||!ih||!frame.opacity)return;
  const {x,y,w,h,angle,opacity}=frame;
  const side=Math.min(w,h)*.022, bottom=Math.min(w,h)*.078;
  const innerW=w-2*side, innerH=h-side-bottom, scale=Math.min(innerW/iw,innerH/ih);
  const width=iw*scale,height=ih*scale;
  g.save();
  // A popped print draws at 80% over the page (ui/pop-picture.js); its credit keeps the print's own fade only.
  g.globalAlpha=opacity*POP_PICTURE_OPACITY;
  g.translate(x+w/2,y+h/2);g.rotate(angle);g.translate(-w/2,-h/2);
  g.shadowColor='rgba(19,12,24,.42)';g.shadowBlur=Math.min(17,side*1.5);
  g.shadowOffsetY=side*.65;
  g.fillStyle='#faf4e7';g.fillRect(0,0,w,h);
  g.shadowColor='transparent';g.shadowBlur=0;g.shadowOffsetY=0;
  g.fillStyle='#e7e0d5';g.fillRect(side,side,innerW,innerH);
  g.drawImage(image,side+(innerW-width)/2,side+(innerH-height)/2,width,height);
  if(photo.credit){
    g.globalAlpha=opacity;
    const fontSize=Math.max(11,Math.min(15,bottom*.63));
    g.font='600 '+fontSize+'px system-ui,sans-serif';
    const labelWidth=g.measureText(photo.credit).width;
    const labelRight=Math.max(w-side,labelWidth+4);
    // Give the small print a legible credit tab as it leaves its source.
    if(bottom<fontSize+4 || labelWidth>innerW){
      g.fillStyle='#faf4e7';
      g.fillRect(labelRight-labelWidth-3,h-bottom,labelWidth+6,fontSize+4);
    }
    g.fillStyle='#302830';g.textAlign='right';g.textBaseline='middle';
    g.fillText(photo.credit,labelRight,h-bottom+Math.max(bottom/2,(fontSize+4)/2));
  }
  g.restore();
}
