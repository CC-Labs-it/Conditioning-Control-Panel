// Sample the ink once per word and size. All charge contacts land on the painted
// letters, including short words, counters, and the space in LET GO.
const inkCache = new Map();
const tau = 2 * Math.PI;
const noise = n => { const v = Math.sin(n * 91.7 + 27.4) * 23417.13; return v - Math.floor(v); };

function sampleInk(g,text,size,width) {
  const key = g.font + ':' + text;
  if (inkCache.has(key)) return inkCache.get(key);
  const tile = g.canvas.ownerDocument.createElement('canvas');
  const scale = 2, w = Math.ceil(width + 6), h = Math.ceil(size * 1.75);
  tile.width = w * scale; tile.height = h * scale;
  const ctx = tile.getContext('2d',{willReadFrequently:true});
  ctx.scale(scale,scale);ctx.font=g.font;ctx.textAlign='center';ctx.textBaseline='middle';
  ctx.fillStyle='#fff';ctx.fillText(text,w/2,h/2);
  const pixels=ctx.getImageData(0,0,tile.width,tile.height).data;
  const top=[],bottom=[];
  for(let x=0;x<tile.width;x+=2){
    let first=-1,last=-1;
    for(let y=0;y<tile.height;y++)if(pixels[(y*tile.width+x)*4+3]>100){
      if(first<0)first=y;
      last=y;
    }
    if(first>=0){top.push([x/scale-w/2,first/scale-h/2]);bottom.push([x/scale-w/2,last/scale-h/2]);}
  }
  const film=tile.ownerDocument.createElement('canvas');
  film.width=tile.width;film.height=tile.height;
  const ink={top,bottom,mask:tile,film,brush:film.getContext('2d'),w,h};
  if(inkCache.size>=160)inkCache.delete(inkCache.keys().next().value);
  inkCache.set(key,ink);return ink;
}

function trace(g,points){
  g.beginPath();g.moveTo(...points[0]);
  for(let i=1;i<points.length;i++)g.lineTo(...points[i]);
}

function discharge(g,points,color,power,size){
  trace(g,points);
  g.globalAlpha=power*.25;g.strokeStyle=color;g.lineWidth=size*.22;g.stroke();
  g.globalAlpha=power*.95;g.lineWidth=size*.10;g.stroke();
  g.strokeStyle='#fff4b1';g.lineWidth=Math.max(.5,size*.032);g.stroke();
}

function bolt(from,to,lift,seed,phase,size) {
  const points=[from];
  for(let k=1;k<8;k++){
    const t=k/8,envelope=Math.sin(Math.PI*t);
    const kink=(noise(seed+k)-.5)*size*.26+Math.sin(phase*3+k*1.8)*size*.065;
    points.push([from[0]+(to[0]-from[0])*t+(noise(seed+17*k)-.5)*size*.16*envelope,
      from[1]+(to[1]-from[1])*t+(lift+kink)*envelope]);
  }
  points.push(to);return points;
}

export function paintCharged(g,{text,size,width,main,other,phase,alpha}){
  const ink=sampleInk(g,text,size,width),{top,bottom}=ink;if(!top.length)return;
  // A dark rim separates the type from the outer discharge, especially on pale themes.
  g.lineWidth=Math.max(1.7,size*.13);g.strokeStyle='#18203d';g.strokeText(text,0,0);
  g.fillStyle=main;g.fillText(text,0,0);
  g.lineWidth=.65;g.strokeStyle=other;g.strokeText(text,0,0);
  const front=(phase*.62)%1;
  // Run plasma through the ink as well as outside it. The glyph mask preserves holes and spaces.
  const {brush,film,mask,w,h}=ink;
  brush.setTransform(1,0,0,1,0,0);brush.clearRect(0,0,film.width,film.height);
  brush.setTransform(2,0,0,2,w,h);
  const metal=brush.createLinearGradient(0,-size*.42,0,size*.43);
  metal.addColorStop(0,'#f4ffff');metal.addColorStop(.22,main);metal.addColorStop(.53,other);
  metal.addColorStop(.68,'#223058');metal.addColorStop(1,main);
  brush.fillStyle=metal;brush.fillRect(-w/2,-h/2,w,h);
  for(let lane=0;lane<2;lane++){
    const yy=(lane-.5)*size*.29,seed=lane*71+text.charCodeAt(0);
    const channel=[];
    for(let k=0;k<=30;k++){
      const xx=(k/30-.5)*w,charge=Math.cos(phase*4+k*.41+lane);
      channel.push([xx,yy+(noise(seed+k)-.5)*size*.32+charge*size*.11]);
    }
    trace(brush,channel);brush.strokeStyle=other;brush.lineWidth=size*.14;brush.stroke();
    brush.lineWidth=Math.max(.65,size*.038);brush.strokeStyle='#fff0a1';brush.stroke();
  }
  const peak=(front-.5)*w,band=brush.createLinearGradient(peak-size*.32,0,peak+size*.32,0);
  band.addColorStop(0,'#ffffff00');band.addColorStop(.50,'#e4ffffaf');band.addColorStop(1,'#ffffff00');
  brush.fillStyle=band;brush.fillRect(peak-size*.32,-h/2,size*.64,h);
  brush.setTransform(1,0,0,1,0,0);brush.globalCompositeOperation='destination-in';brush.drawImage(mask,0,0);
  brush.globalCompositeOperation='source-over';
  g.drawImage(film,-w/2,-h/2,w,h);
  for(let i=0;i<top.length;i++){
    const position=i/top.length,distance=Math.abs(position-front);
    const intensity=Math.max(0,1-Math.min(distance,1-distance)/.14);
    if(!intensity)continue;
    const [x,y]=top[i];
    g.globalAlpha=alpha*intensity;g.fillStyle='#fff4af';
    g.fillRect(x-.45,y,1,Math.min(bottom[i][1]-y,size*.19));
  }
  g.lineCap='round';
  for(let lane=0;lane<3;lane++){
    const bank=lane===1?bottom:top,sign=lane===1?1:-1;
    const tick=phase*(lane===1?.83:1.11)+lane*.36;
    const epoch=Math.floor(tick),u=tick-epoch;
    const strength=Math.sin(Math.PI*u)**.8;
    const seed=epoch*47+lane*151+text.charCodeAt(0);
    const length=lane===0?.48:.25;
    const a=(.02+noise(seed)*(.94-length)),b=a+length;
    const contact=t=>bank[Math.min(bank.length-1,Math.floor(t*bank.length))];
    const from=contact(a),to=contact(b),color=lane===1?'#ffbb39':'#ffdf55';
    const points=bolt(from,to,sign*size*(lane===0?.38:.23),seed,phase,size);
    discharge(g,points,color,alpha*strength,size);
    const fork=points[3],root=points[5];
    const tip=[fork[0]+(noise(seed+4)-.5)*size*.7,fork[1]+sign*size*(.2+.16*noise(seed+8))];
    discharge(g,[root,fork,[tip[0]-size*.09,tip[1]-sign*size*.07],tip],color,alpha*strength*.7,size*.7);
    // Charge runs down the bolt into the next letter and sheds a few fading ions.
    const along=u*(points.length-1),i=Math.min(points.length-2,Math.floor(along)),f=along-i;
    const px=points[i][0]+(points[i+1][0]-points[i][0])*f,py=points[i][1]+(points[i+1][1]-points[i][1])*f;
    g.globalAlpha=alpha*strength;g.fillStyle='#fff5bb';g.beginPath();g.arc(px,py,size*.052,0,tau);g.fill();
    for(let j=0;j<3;j++){
      const age=(u+j/3)%1,angle=sign*(.4+noise(seed+7*j)*2.3);
      const d=age*size*.42;
      g.globalAlpha=alpha*strength*(1-age);g.fillStyle=j%2?'#ffd540':'#fff1a0';
      g.fillRect(tip[0]+Math.cos(angle)*d,tip[1]+Math.sin(angle)*d,1+size*.018,1+size*.018);
    }
  }
}
