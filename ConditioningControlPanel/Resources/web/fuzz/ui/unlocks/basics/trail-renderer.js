import {createFollowingWords} from './following-words.js';
import {createCollectionTrail} from './collection-trail.js';

const styles = {
  trail: { spacing: 3, spread: 6, size: 1, life: 650, capacity: 384, starEvery: 5 },
  starburst: { spacing: 2.3, spread: 14, size: 1.65, life: 850, capacity: 700, starEvery: 3 },
  word_comet: { spacing: 1.8, spread: 24, size: 1.35, life: 1000, capacity: 1100, starEvery: 3 },
};
const noise = index => { const n = Math.sin(index * 127.1 + 311.7) * 43758.5453; return n - Math.floor(n); };

// Input and painting use the same active clock. One buffer serves the game and the shop sample.
export function createTrailRenderer(tier, stage=1) {
  const overlay=createFollowingWords();
  const particles=styles[tier]?createDust(tier,overlay.move):createCollectionTrail(tier,stage,overlay.move);
  return {
    move:particles.move,
    leave(){particles.leave();overlay.leave();},
    clear(){particles.clear();overlay.clear();},
    draw(g,now,palette,withWords=tier==='word_comet',look){
      const strength=withWords?.6:1;
      const count=styles[tier]?particles.draw(g,now,palette,strength):particles.draw(g,now,strength);
      if(withWords)overlay.draw(g,now,palette,look);
      return count || Number(withWords&&overlay.alive(now));
    }
  };
}
function createDust(tier,onEmit) {
  const style = styles[tier], comet = tier === 'word_comet';
  const dust = Array.from({length: style.capacity}, () => ({}));
  let serial = 0, count = 0, last = null, carry = 0;
  function emit(x, y, at) {
    onEmit(x,y,at);
    const i = serial++, star = i % style.starEvery === 0;
    Object.assign(dust[i % dust.length], {
      x: x + (noise(i) - .5) * style.spread, y: y + (noise(i + 2) - .5) * style.spread,
      size: style.size * (star ? 2.5 + noise(i + 4) * 2 : .55 + noise(i + 3) * .95),
      at, star, life: tier === 'trail' && star ? 850 : style.life, index: i,
      vx: (noise(i + 8) - .5) * 18, vy: -4 - noise(i + 9) * 12, angle: noise(i + 5) * Math.PI * 2,
    });
    count = Math.min(count + 1, dust.length);
  }
  function move(x, y, now) {
    if (!last || now - last.at >= 950) {
      carry = 0;
      last = {x,y,at:now}; emit(x,y,now);
      return;
    }
    const dx = x - last.x, dy = y - last.y, distance = Math.hypot(dx,dy);
    if (!distance) return;
    const spacing = Math.max(style.spacing, distance/120);
    for (let along = spacing - carry; along <= distance; along += spacing) {
      const f = along / distance;
      emit(last.x + dx*f, last.y + dy*f, now);
    }
    carry = (carry + distance) % spacing;
    last = {x,y,at:now};

  }
  function draw(g, now, palette, strength) {
    let visible = 0;
    for(let n=0;n<count;n++) {
      const p=dust[(serial-count+n)%dust.length],age=now-p.at,t=age/p.life;
      if(t>=1)continue;
      visible++;
      const radius=p.size*(1-t*.65),shade=p.index%10;
      let color=p.index%2 ? palette.b : palette.a;
      if(comet && shade<8) color=shade<4?'#ffd76a':'#64eaff';
      if(tier==='trail' && p.star && p.index%2) color='#fff4c9';
      g.save();g.translate(p.x+p.vx*age/1000,p.y+p.vy*age/1000);g.rotate(p.angle+t*.5);
      g.globalAlpha=(1-t)**1.5*(p.star?1:.8)*(comet?.42:1)*strength;g.fillStyle=color;g.beginPath();
      if(p.star){
        g.shadowColor=color;g.shadowBlur=comet?1:tier==='starburst'?4:0;
        for(let k=0;k<8;k++){const a=k*Math.PI/4,r=k%2?radius*(comet?.12:.22):radius;g.lineTo(Math.cos(a)*r,Math.sin(a)*r);}
        g.closePath();
      }else g.arc(0,0,radius,0,Math.PI*2);
      g.fill();g.restore();
    }
    if(!visible)count=0;
    return visible;
  }
  return {move,draw,leave(){last=null;},clear(){count=0;last=null;}};
}
