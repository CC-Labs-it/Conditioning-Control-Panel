import {DEMOS} from './kit.js';
import {createTrailRenderer} from '../unlocks/basics/trail-renderer.js';

// Register after the generated defaults so even the original Stardust card uses the live effect.
export function registerTrailDemos(){
  for(const tier of ['trail','starburst','word_comet']){
    DEMOS[tier]=kit=>{
      kit.page('#19152c');
      const canvas=kit.el.ownerDocument.createElement('canvas');
      canvas.width=kit.W;canvas.height=kit.H;kit.el.appendChild(canvas);
      const g=canvas.getContext('2d'),trail=createTrailRenderer(tier);
      const css=getComputedStyle(document.body);
      const colors={a:css.getPropertyValue('--niche').trim()||'#a58bff',b:css.getPropertyValue('--niche-2').trim()||'#ff4fa8'};
      kit.frame((dt,time)=>{
        const x=kit.W*(.5+.38*Math.sin(time*3.4)),y=kit.H*(.5+.26*Math.sin(time*5));
        trail.move(x,y,time*1000);g.clearRect(0,0,kit.W,kit.H);
        trail.draw(g,time*1000,colors);
        g.fillStyle=colors.a;g.beginPath();g.arc(x,y,2.5,0,Math.PI*2);g.fill();
      });
    };
  }
}
