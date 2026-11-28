// Percentage outlines traced from the shipped transparent cabinet art. A strip
// along each segment connects the ink to its unprinted back. The live face sits
// above the entire assembly and always keeps its labels, reels and controls.
const OUTLINES = {
  dispenser:'45,0 39,2 30,8 25,11 21,15 13,21 9,27 7,38 12,45 17,49 16,52 11,56 12,63 7,71 7,78 11,81 15,82 16,84 26,84 28,82 72,82 74,84 84,84 85,82 88,81 93,78 93,71 88,63 89,57 83,49 88,45 93,38 91,27 87,21 78,15 75,11 70,9 69,7 66,7 60,2 54,0',
  wheel:'34,3 33,4 33,9 22,14 13,22 8,30 5,37 4,50 9,62 17,71 33,83 33,86 12,86 11,88 11,96 13,97 88,97 89,95 88,86 92,86 92,83 87,81 90,80 89,73 82,73 90,64 95,52 96,40 92,28 88,23 82,17 75,12 67,9 66,3',
  mini_slots:'18,15 16,16 15,20 15,47 16,51 13,53 13,57 16,59 35,59 36,80 29,81 26,86 27,88 28,89 65,88 66,86 64,81 57,80 57,59 76,59 78,58 78,53 76,51 76,49 80,48 80,46 84,46 85,44 82,41 82,27 77,27 78,34 78,40 77,42 77,21 76,16 74,15 53,15 53,12 51,11 40,11 39,12 39,15',
  toy_box:'4,6 1,10 0,16 0,88 2,93 5,97 11,100 89,100 93,99 97,96 99,91 100,87 99,16 98,9 95,5 90,2 82,2 80,0 73,0 71,2 29,2 27,0 20,0 18,2 9,2',
};

export function createCabinetStock(doc,root,surface) {
  const art=surface.artRect,trace=OUTLINES[surface.owner];
  if(!art||!trace||surface.kind!=='toy')return null;
  const holder=doc.createElement('div');
  holder.style.cssText='position:absolute;inset:0;transform-style:preserve-3d;pointer-events:none;';
  root.append(holder);
  const W=surface.w,H=surface.h;
  function cut(points,material){
    const back=doc.createElement('div');back.className='bt-prop-back';
    // The back faces away. Mirror its clipping path before the half-turn so
    // asymmetric parts, such as the slot lever, meet the very same edge.
    back.style.clipPath='polygon('+points.map(([x,y])=>(W-x)+'px '+y+'px').join(',')+')';
    back.style.transform='translateZ(calc(0px - var(--bt-'+material+'-depth))) rotateY(180deg)';
    back.style.opacity='var(--bt-stock-alpha)';
    holder.append(back);
    for(let i=0;i<points.length;i++){
      const [x,y]=points[i], [toX,toY]=points[(i+1)%points.length];
      const dx=toX-x,dy=toY-y,length=Math.hypot(dx,dy);
      if(length<.1)continue;
      const strip=doc.createElement('div');strip.className='bt-prop-side bt-prop-edge-y';
      const angle=Math.atan2(dy,dx);
      Object.assign(strip.style,{
        left:x+'px',top:y+'px',width:(length+.15)+'px',height:'100px',outline:'none',
        opacity:'var(--bt-stock-alpha)',transformOrigin:'0 0',
        transform:'rotateZ('+angle+'rad) rotateX(-90deg) scaleY(var(--bt-'+material+'-ratio))',
        backgroundImage:'linear-gradient(rgba(35,29,43,'+(.05+.18*(.5+.5*Math.cos(angle)))+'),rgba(35,29,43,.04)),'+
          'linear-gradient(0deg,#baabad,transparent 7%,transparent 93%,#b4b7be),'+
          'repeating-linear-gradient(0deg,#9a9697 0px,#9e999a 1px,#aaa6a6 2.5px,#a49e9f 4px)',
      });
      holder.append(strip);
    }
  }
  function strip(x,y,w,h){
    if(w<=1||h<=1)return;
    cut([[x,y],[x+w,y],[x+w,y+h],[x,y+h]],'thin');
  }
  const points=trace.split(' ').map(pair=>{
    const [x,y]=pair.split(',').map(Number);
    return [art.x+art.w*x*.01,art.y+art.h*y*.01];
  });
  cut(points,'shell');
  // The dispenser tray and its status sit below the globe. Unsupported text
  // remains visible in the front plane; these thin slips carry it in profile.
  if(surface.owner==='dispenser'){
    strip(art.x+art.w*.23,art.y+art.h*.853,art.w*.54,art.h*.045);
    strip(art.x+art.w*.055,art.y+art.h*.912,art.w*.89,art.h*.085);
  }
  strip(.04*W,0,.92*W,Math.max(0,art.y-1));
  const bottom=Math.max(0,art.y+art.h);
  strip(.025*W,bottom,.95*W,H-bottom);
  return {
    pose(thickness,alpha){
      const thin=Math.min(thickness*.16,Math.max(2,W*.028));
      holder.style.setProperty('--bt-shell-depth',thickness+'px');
      holder.style.setProperty('--bt-shell-ratio',String(thickness/100));
      holder.style.setProperty('--bt-thin-depth',thin+'px');
      holder.style.setProperty('--bt-thin-ratio',String(thin/100));
      holder.style.setProperty('--bt-stock-alpha',String(alpha));
    }
  };
}
