import {trailCollection} from '../../../data/trail-collection.js';
export function createCollectionTrail(id,stage,onEmit){
const family=trailCollection.findIndex(item=>item.id===id),level=stage-1;
const TAU=Math.PI*2,life=1500;
const noise=n=>{const z=Math.sin(n*127.1+311.7)*43758.5453;return z-Math.floor(z);};
let g,time=0,strength=1,samples=[],last=null,serial=0,carry=0;
let accents=[],heading=null,charge=0,lastAccent=-1000;
function emit(x,y,nx,ny){
 samples.push({x,y,t:time,id:serial++,nx,ny});
 onEmit?.(x,y,time);
}
function move(x,y,now){
 if(now-time>life)leave();time=now;
 if(!last){last={x,y};emit(x,y,0,-1);return;}
 const dx=x-last.x,dy=y-last.y,d=Math.hypot(dx,dy);if(d<.1)return;
 const direction=Math.atan2(dy,dx);
 const turn=heading===null?0:Math.atan2(Math.sin(direction-heading),Math.cos(direction-heading));
 charge+=Math.min(28,d)*(1+Math.abs(turn)*2.8);
 if((Math.abs(turn)>.37||charge>105-level*12)&&time-lastAccent>145){
  accents.push({x,y,t:time,id:serial,angle:direction,sign:turn<0?-1:1});
  if(accents.length>16)accents.shift();charge=0;lastAccent=time;
 }
 heading=direction;
 for(let s=5-carry;s<=d;s+=5)emit(last.x+dx*s/d,last.y+dy*s/d,-dy/d,dx/d);
 carry=(carry+d)%5;last={x,y};if(samples.length>340)samples.splice(0,samples.length-340);
}
function opacity(v){g.globalAlpha=strength*Math.min(1,Math.max(0,v));}
function dot(x,y,r,color,alpha){opacity(alpha);g.fillStyle=color;g.beginPath();g.arc(x,y,r,0,TAU);g.fill();}
function ellipse(x,y,rx,ry,rot,color,alpha,fill=false,width=1){opacity(alpha);g.beginPath();g.ellipse(x,y,rx,ry,rot,0,TAU);if(fill){g.fillStyle=color;g.fill();}else{g.lineWidth=width;g.strokeStyle=color;g.stroke();}}
function line(x,y,ex,ey,color,alpha,width=1){opacity(alpha);g.strokeStyle=color;g.lineWidth=width;g.beginPath();g.moveTo(x,y);g.lineTo(ex,ey);g.stroke();}
function star(x,y,r,col,a){line(x-r,y,x+r,y,col,a);line(x,y-r,x,y+r,col,a);dot(x,y,1.2,col,a);}
const glows=new Map();
function glow(x,y,r,color,alpha){
 let sprite=glows.get(color);
 if(!sprite){
  sprite=document.createElement('canvas');sprite.width=sprite.height=48;
  const brush=sprite.getContext('2d'),light=brush.createRadialGradient(24,24,0,24,24,24);
  light.addColorStop(0,color);light.addColorStop(.3,color+'99');light.addColorStop(1,color+'00');
  brush.fillStyle=light;brush.fillRect(0,0,48,48);glows.set(color,sprite);
 }
 opacity(alpha);g.drawImage(sprite,x-r,y-r,2*r,2*r);
}
function arc(x,y,r,start,span,color,alpha,width=1){
 opacity(alpha);g.strokeStyle=color;g.lineWidth=width;g.beginPath();g.arc(x,y,r,start,start+span);g.stroke();
}
function leaf(x,y,length,width,angle,color,alpha){
 g.save();g.translate(x,y);g.rotate(angle);opacity(alpha);g.fillStyle=color;g.beginPath();g.moveTo(0,0);
 g.quadraticCurveTo(length*.35,-width,length,0);g.quadraticCurveTo(length*.5,width*.75,0,0);g.fill();g.restore();
}
function butterfly(x,y,size,turn,phase,a,b,fade){
 const beat=.25+.75*Math.abs(Math.sin(phase));
 g.save();g.translate(x,y);g.rotate(turn);g.scale(beat,1);
 for(const side of [-1,1]){
  g.save();g.scale(side,1);
  leaf(0,0,size*1.4,size*.65,-.62,a,fade*.87);
  leaf(0,1,size*.95,size*.65,.56,b,fade*.77);
  line(1,0,size*.88,-size*.49,'#effff6',fade*.72,.75);
  dot(size*.85,-size*.65,1.15,'#fff4c8',fade);
  g.restore();
 }
 g.restore();line(x,y-size*.29,x,y+size*.31,'#d9d5f4',fade,1.1);
}
function drawAccent(p,a,b){
 const t=Math.max(0,(time-p.t)/950);if(t>=1)return;
 const fade=(1-t)**1.3,expand=1-(1-t)**2;
 g.save();g.translate(p.x,p.y);g.rotate(p.angle);
 const count=6+level*3,sign=p.sign;
 if(family===0){
  for(let j=0;j<count;j++){
   const s=j/count,ang=sign*(.45+1.8*s),d=(10+21*s)*expand;
   const x=-d*Math.cos(ang),y=d*Math.sin(ang);
   leaf(x,y,4+6*fade,1.5,Math.PI-ang,s<.5?a:b,fade*.62);
   if(j%3===0)star(x,y,2.1,a,fade*.8);
  }
 }else if(family===1){
  for(let j=0;j<count;j++){
   const ang=-1.3+j*2.6/count,r=(8+24*noise(p.id+j))*expand;
   const x=-r*Math.cos(ang),y=r*Math.sin(ang);
   line(x,y,x-4*Math.cos(ang),y+4*Math.sin(ang),j%2?a:'#ffe5ba',fade*.95,1.1);
  }
  glow(0,0,15*(1-t)+3,b,fade*.75);
 }else if(family===2){
  for(let j=0;j<count+3;j++){
   const ang=j*2.39996,dist=(6+23*noise(p.id+j))*expand,rad=1.4+noise(j+7)*3;
   const x=Math.cos(ang)*dist,y=Math.sin(ang)*dist;
   dot(x,y,rad,j%3?a:b,fade*.66);dot(x-rad*.3,y-rad*.4,rad*.35,'#fff3ff',fade*.8);
  }
 }else if(family===3){
  const r=5+16*expand;arc(0,0,r,t*3,TAU*.87,b,fade*.65);
  for(let j=0;j<count;j++){const ang=TAU*j/count+t*.5;star(r*Math.cos(ang),r*Math.sin(ang),j%3?1.7:3.2,j%2?a:b,fade*.9);}
 }else if(family===4){
  for(let j=0;j<count;j++){const ang=j*2.4+t*2*sign,r=(8+17*noise(j+p.id))*expand;
   const x=Math.cos(ang)*r,y=Math.sin(ang)*r;glow(x,y,6,a,fade*.65);dot(x,y,1.2,'#fff8ad',fade);
  }
 }else if(family===5){
  const r=3+29*expand;arc(0,0,r,0,TAU,a,fade*.75);
  arc(0,0,r*.72,0,TAU,b,fade*.65);
  for(let j=0;j<count;j++){const ang=TAU*j/count,dist=r*(.97+.18*Math.sin(t*5));dot(dist*Math.cos(ang),dist*Math.sin(ang),1.4,j%2?a:'#fcf4ff',fade);}
 }else if(family===6){
  for(let j=0;j<count;j++){const ang=Math.PI+(j/(count-1)-.5)*1.9;
   const x=Math.cos(ang)*24*expand,y=Math.sin(ang)*24*expand;
   leaf(x,y,10+5*fade,2.1,ang,j%3?a:b,fade*.91);
  }
 }else{
  for(let j=0;j<2+level;j++){const ang=j*2.4+sign*t,r=(10+8*j)*expand;
   butterfly(Math.cos(ang)*r,Math.sin(ang)*r,4+j,ang*.25,time*.007+j,a,b,fade);
  }
 }
 g.restore();
}
function draw(context,now,opacityScale=1){
 g=context;time=now;strength=opacityScale;samples=samples.filter(p=>time-p.t<life);accents=accents.filter(p=>time-p.t<950);
 const [a,b]=trailCollection[family].colors;let prev=null,anchor=null,older=null;
 for(const p of samples){
  const age=Math.max(0,time-p.t)/1000,fade=Math.max(0,1-age/(life/1000));if(!fade)continue;
  const n=p.id,rand=noise(n),drift=n*.15-time*.0018;
  if(family===0){
   const offset=(level?10:3)*Math.sin(drift),x=p.x+p.nx*offset,y=p.y+p.ny*offset;
   const xx=p.x-p.nx*offset,yy=p.y-p.ny*offset;
   if(prev){if(level===2){opacity(fade*.36);g.fillStyle=n%24<12?a:b;g.beginPath();g.moveTo(prev.x,prev.y);g.lineTo(x,y);g.lineTo(xx,yy);g.lineTo(prev.xx,prev.yy);g.fill();line(x,y,xx+p.nx*6,yy+p.ny*6,b,fade*.24);}
    line(prev.x,prev.y,x,y,a,fade*.86,2.2);if(level)line(prev.xx,prev.yy,xx,yy,b,fade*.83,2);}
   if(prev){
    const fringe=5+5*Math.sin(drift*.58);
    line(prev.x,prev.y,x,y,a,fade*.11,10);
    line(x,y,xx+p.nx*fringe,yy+p.ny*fringe,b,fade*.32,1.3);
    if(level===1&&n%4===0)line(x,y,xx,yy,'#f2fff3',fade*.45,.8);
    if(level===2)line(x,y,x+p.nx*(12+18*rand),y+p.ny*(12+18*rand),a,fade*.26,1);
   }
   for(let j=0;j<=level+1;j++){
    const seed=n*7+j,side=noise(seed+41)-.5,d=side*(14+age*46)+Math.sin(drift+j)*4;
    const mx=p.x+p.nx*d-age*(3+rand*8),my=p.y+p.ny*d-age*(5+8*noise(seed));
    dot(mx,my,.45+noise(seed+14)*1.15,j%2?a:b,fade*(.26+rand*.56));
    if(seed%17===0)star(mx,my,2+2*rand,'#e5ffef',fade*.72);
   }
   prev={x,y,xx,yy};
  }else if(family===1&&n%5===0){
   const x=Math.round(p.x/8)*8,y=Math.round(p.y/8)*8;
   if(prev){const segments=(dy,col)=>{line(prev.x,prev.y+dy,x,prev.y+dy,col,fade*.8,1.6);line(x,prev.y+dy,x,y+dy,col,fade*.8,1.6);};
    segments(0,a);if(level){segments(6,b);dot(x,y,2.5,a,fade*.9);}
    if(level===2){const u=(time/550+n*.18)%1;dot(prev.x+(x-prev.x)*u,prev.y,3,'#ecffff',fade);dot(x,prev.y+6+(y-prev.y)*u,2,'#ffe2f2',fade);}}
   if(n%10===0){
    const dir=n%20===0?1:-1,reach=7+rand*(10+level*7),yy=y+dir*reach,xx=x+dir*(6+rand*5);
    line(x,y,x,yy,b,fade*.63);line(x,yy,xx,yy,b,fade*.63);
    opacity(fade*.9);g.strokeStyle=a;g.lineWidth=1;g.strokeRect(xx-2,yy-2,4,4);
    glow(xx,yy,6+level*2,a,fade*.5);
    for(let j=0;j<3+level;j++){
     const k=noise(n+j*5),travel=(4+age*30)*k,sx=xx+dir*travel,sy=yy-8*k-age*(5+j);
     line(sx,sy,sx+dir*2.4,sy-1.6,j%2?a:'#fee5b6',fade*.8,.85);
    }
    if(level===2)arc(xx,yy,3+(time/50+n)%12,0,TAU,a,fade*.32);
   }
   prev={x,y};
  }else if(family===2){
   const x=p.x,y=p.y,r=2.2+rand*3.2+age*2;
   if(prev)line(prev.x,prev.y,x,y,a,fade*.65,r);if(n%3===0)dot(x,y,r,a,fade*.54);
   if(level&&n%3===0){const d=(4+rand*12)*(n%2?1:-1)*(1+age);dot(x+p.nx*d,y+p.ny*d,1+rand*3,b,fade*.62);}
   if(level===2&&n%6===0){const k=8+age*16;dot(x,y,k,a,fade*.16);dot(x+p.nx*k*.55,y+p.ny*k*.55,k*.65,b,fade*.23);ellipse(x,y,k,k*.55,rand*TAU,b,fade*.45);}
   if(n%2===0){
    const driftSide=rand>.5?1:-1,d=driftSide*(4+noise(n+2)*17+age*9);
    const qx=x+p.nx*d,qy=y+p.ny*d,rad=1+rand*2;
    dot(qx,qy,rad,b,fade*.7);dot(qx-.4,qy-.7,rad*.32,'#ffeeff',fade*.76);
    if(n%4===0){
     for(let j=0;j<4;j++){
      const angle=TAU*noise(n*9+j),reach=5+noise(n+j*6)*12+age*7;
      const bx=qx+reach*Math.cos(angle),by=qy+reach*Math.sin(angle);
      dot(bx,by,.6+noise(n+j)*.95,j%2?a:b,fade*.65);
      if(level)line(qx,qy,bx,by,b,fade*.38,.7);
     }
     if(level===2)arc(qx,qy,rad+age*5,drift,.95*TAU,'#fbdcff',fade*.48);
    }
   }
   prev={x,y};
  }else if(family===3&&n%3===0){
   const d=(rand-.5)*42,x=p.x+p.nx*d,y=p.y+p.ny*d;
   if(level&&anchor)line(anchor.x,anchor.y,x,y,b,fade*.6);
   if(level===2&&older){line(older.x,older.y,x,y,b,fade*.35);opacity(fade*.08);g.fillStyle=b;g.beginPath();g.moveTo(older.x,older.y);g.lineTo(anchor.x,anchor.y);g.lineTo(x,y);g.fill();}
   if(n%12===0){star(x,y,3+rand*3,a,fade);if(level===2)ellipse(x,y,9,9,0,a,fade*.65);}else dot(x,y,1+rand*1.5,b,fade);
   glow(x,y,8+rand*5,n%2?a:b,fade*.5);
   for(let j=0;j<4+level*2;j++){
    const ang=TAU*noise(n+j*16),r=5+noise(n*8+j)*20+age*6;
    dot(x+Math.cos(ang)*r,y+Math.sin(ang)*r,.5+noise(n+5*j),j%3?b:a,fade*.7);
   }
   if(n%12===0){const theta=time*.0009+n;
    arc(x,y,7+age*3,theta,1.2,a,fade*.65);
    if(level===2){arc(x,y,13,theta+2,3,b,fade*.68);star(x+13*Math.cos(theta),y+13*Math.sin(theta),2,'#fff4d1',fade);}
   }
   older=anchor;anchor={x,y};
  }else if(family===4&&n%3===0){
   const flies=level+1;for(let j=0;j<flies;j++){
    const angle=n*.6+j*2.1+age*(2+j),rad=(3+j*5+rand*6)*(1+age),x=p.x+Math.cos(angle)*rad,y=p.y+Math.sin(angle)*rad-age*12;
    const pulse=.64+.36*Math.sin(n*.7+age*4.3);
    glow(x,y,8+rand*6,j%2?b:a,fade*pulse*.65);dot(x,y,1.8,'#ffffd9',fade*.95);
    const wing=.65+Math.abs(Math.cos(n+age*11))*1.5;
    ellipse(x-1.7,y-1.8,2.8,wing,-.65,b,fade*.57,true);ellipse(x+1.7,y-1.8,2.8,wing,.65,b,fade*.57,true);
    for(let k=0;k<2+level;k++){const u=noise(n+k*3);dot(x+Math.cos(angle+k)*u*(8+age*15),y+Math.sin(angle+k)*u*9-age*6,.5+u,a,fade*.66);}
    if(level){ellipse(x-1,y-2,2.6,1,-.7,b,fade*.46,true);ellipse(x+1,y-2,2.6,1,.7,b,fade*.46,true);}
   }
  }else if(family===5&&n%6===0){
   const r=5+age*30,phase=TAU*rand+age*.7;
   arc(p.x,p.y,r,0,TAU,a,fade*.22,4);arc(p.x,p.y,r,0,TAU,a,fade*.8,1.1);
   arc(p.x,p.y,r,phase,1.15,'#e8ffff',fade);
   for(let j=0;j<5+level*2;j++){
    const angle=phase+j*2.39996,spread=r+(j%3)*(2+age*4);
    const sx=p.x+Math.cos(angle)*spread,sy=p.y+Math.sin(angle)*spread;
    dot(sx,sy,.7+noise(n+j)*1.3,j%2?a:'#f8e3ff',fade*.8);
    if(level===2&&j===0)star(sx,sy,3,'#fcffeb',fade);
   }
   if(level){arc(p.x,p.y,r*.64,0,TAU,b,fade*.65);arc(p.x,p.y,r*1.36,phase+2,TAU,b,fade*.35);}
   if(level===2){dot(p.x,p.y,r*.96,b,fade*.07);arc(p.x,p.y,r+2.5,phase+2,2.2,'#ff9fe8',fade*.85);arc(p.x,p.y,r-1.3,phase-1,1.6,'#ffd98a',fade*.85);}
  }else if(family===6){
   const t=age,x=p.x+Math.sin(n*.42+t*3.1)*t*12,y=p.y-t*(17+rand*15);
   if(prev)line(prev.x,prev.y,x,y,b,fade*.4,3+rand*3);
   leaf(x,y,5+rand*10,2+rand*2,-1.4+Math.sin(drift)*.6,n%3?a:b,fade*.8);
   if(n%4===0)glow(x,y,13,a,fade*.38);
   for(let j=0;j<2+level;j++){const u=noise(n*3+j),ex=x+(u-.5)*(8+55*t),ey=y-t*(10+u*26);line(ex,ey,ex-1,ey+2+u*2,a,fade*.88,.85);}
   if(level&&n%6===0){const side=n%12?1:-1,angle=Math.atan2(p.ny*side,p.nx*side)-.28;
    leaf(x,y,15+t*9,4.2,angle,b,fade*.76);line(x,y,x+Math.cos(angle)*13,y+Math.sin(angle)*13,a,fade*.9);
    if(level===2)for(let k=0;k<4;k++)leaf(x,y,18-k*2,2.2,angle+Math.PI+k*.19,k%2?a:b,fade*.75);}
   prev={x,y};
  }else if(family===7){
   const angle=drift+n*.18,dr=(rand-.5)*18*(1+age);
   const x=p.x+p.nx*dr+Math.cos(angle)*age*5,y=p.y+p.ny*dr-age*(10+rand*14);
   leaf(x,y,1.5+rand*3,1.1,angle,n%2?a:b,fade*.77);
   if(n%4===0)dot(x+3*Math.cos(angle),y+4*Math.sin(angle),.9,'#ffe5a8',fade);
   if(n%(level===0?17:level===1?12:8)===0){
    const phase=n*.3+age*3,reach=level?9+age*13:3+age*5;
    const bx=x+Math.cos(phase)*reach,by=y+Math.sin(phase)*reach;
    glow(bx,by,12,b,fade*.29);
    butterfly(bx,by,5+rand*3,Math.sin(phase)*.55,n+age*8,a,b,fade);
    if(level>=1)butterfly(bx+10,by-9,4,.5,n+age*10,b,a,fade*.85);
    if(level===2){arc(bx,by,reach,phase+1,1.5,a,fade*.35);star(bx+3,by-7,2,'#fff9dd',fade);}
   }
  }
 }
 for(const accent of accents)drawAccent(accent,a,b);
 if(samples.length&&last&&family===4&&level===2){for(let j=0;j<5;j++){const t=time*.0027+j*TAU/5;dot(last.x+Math.cos(t)*19,last.y+Math.sin(t)*10,1.8,a,.82);}}

 g.globalAlpha=1;return samples.length;
}

function leave(){last=null;heading=null;carry=charge=0;}
function clear(){leave();samples=[];accents=[];lastAccent=-1000;}
return {move,draw,leave,clear};
}
