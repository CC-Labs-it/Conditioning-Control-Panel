import {mkdir,writeFile} from 'node:fs/promises';
import {createRequire} from 'node:module';
import {fileURLToPath} from 'node:url';
import {resolve} from 'node:path';

// Run against the local game preview. All colors, shapes, type, and particles are
// painted by gameplay. A separate gesture frames each effect at shop-card scale.
const {chromium}=createRequire(process.env.PLAYWRIGHT_PACKAGE || 'C:/wt-fe/e2e/package.json')('playwright');
const output=process.argv[2] || fileURLToPath(new URL('../assets/shop/trails/',import.meta.url));
const game=process.env.FUZZ_URL || 'http://localhost:8807/fuzz/index.html';
const browser=await chromium.launch({channel:'chrome',headless:true,args:['--mute-audio']});
try{
  const page=await browser.newPage();
  await page.goto(game);
  const cards=await page.evaluate(async()=>{
    const {trailCollection}=await import('./data/trail-collection.js');
    const {createTrailRenderer}=await import('./ui/unlocks/basics/trail-renderer.js');
    // Cubic strokes are specified in thumbnail coordinates. Piecewise straight
    // strokes let Circuit keep its characteristic right angles.
    const studies={
      trail:      {points:[[-5,79],[73,90],[84,1],[150,39],[180,57],[209,63],[251,25]], zoom:1.7, time:250},
      starburst:  {points:[[-9,70],[55,80],[86,24],[140,40],[181,55],[201,63],[249,32]], zoom:1.36,time:265},
      word_comet: {points:[[12,73],[43,47],[64,27],[114,47],[167,71],[212,69],[251,29]], zoom:.86,time:820,age:85},
      aurora:     {points:[[-4,74],[59,75],[73,15],[133,42],[181,68],[213,54],[247,25]], zoom:1.25,time:490},
      circuit:    {points:[[7,63],[45,63],[60,43],[111,43],[125,59],[163,59],[184,33],[251,33]], zoom:1.22,time:400,steps:true},
      ink:        {points:[[1,72],[37,70],[95,13],[146,34],[194,49],[194,71],[252,50]], zoom:1.43,time:490},
      atlas:      {points:[[14,63],[60,32],[89,29],[136,54],[173,81],[184,66],[243,31]], zoom:1.30,time:440},
      fireflies:  {points:[[7,67],[58,41],[84,35],[122,46],[176,61],[199,60],[237,40]], zoom:1.55,time:450},
      ripples:    {points:[[35,60],[76,70],[94,26],[135,46],[185,75],[203,46],[240,41]], zoom:1.18,time:460,age:85},
      phoenix:    {points:[[1,83],[57,93],[86,51],[134,52],[185,55],[210,75],[250,30]], zoom:1.32,time:500},
      glasswing:  {points:[[8,74],[62,76],[79,29],[129,41],[166,53],[204,67],[240,39]], zoom:1.48,time:420}
    };
    const cards=[];
    for(const id of ['trail','starburst','word_comet',...trailCollection.map(item=>item.id)]){
      const shot=studies[id];
      const stages=trailCollection.some(item=>item.id===id)?3:1;
      for(let stage=1;stage<=stages;stage++){
        const c=document.createElement('canvas');
        c.width=720;c.height=360;
        const g=c.getContext('2d',{willReadFrequently:true}),trail=createTrailRenderer(id,stage);
        const scale=shot.zoom;
        g.setTransform(2*scale,0,0,2*scale,100,80);
        const count=88, points=shot.points;
        for(let i=0;i<=count;i++){
          const u=i/count, segments=shot.steps?points.length-1:(points.length-1)/3;
          const section=Math.min(segments-1,Math.floor(u*segments));
          const t=u*segments-section, s=1-t, p=shot.steps?section:section*3;
          const coord=k=>shot.steps ? s*points[p][k]+t*points[p+1][k] :
            s**3*points[p][k]+3*s*s*t*points[p+1][k]+3*s*t*t*points[p+2][k]+t**3*points[p+3][k];
          trail.move(coord(0)/scale,coord(1)/scale,1000+i*shot.time/count);
        }
        trail.draw(g,1000+shot.time+(shot.age||18),{a:'#bba3ff',b:'#ff80c2'},id==='word_comet');
        // Fit the complete specimen, including wings, forks, and word edges.
        const rgba=g.getImageData(0,0,c.width,c.height).data;
        let left=c.width,top=c.height,right=0,bottom=0;
        for(let y=0;y<c.height;y++)for(let x=0;x<c.width;x++){
          if(rgba[(y*c.width+x)*4+3]<6)continue;
          left=Math.min(left,x);right=Math.max(right,x);top=Math.min(top,y);bottom=Math.max(bottom,y);
        }
        const w=right-left+9,h=bottom-top+9;
        const card=document.createElement('canvas');card.width=520;card.height=200;
        const ctx=card.getContext('2d'),fit=Math.min(502/w,188/h);
        ctx.drawImage(c,left-4,top-4,w,h,(520-w*fit)/2,(200-h*fit)/2,w*fit,h*fit);
        cards.push({name:id+'-'+stage,data:card.toDataURL('image/webp',.96).split(',')[1]});
      }
    }
    return cards;
  });
  await mkdir(output,{recursive:true});
  for(const card of cards)await writeFile(resolve(output,card.name+'.webp'),Buffer.from(card.data,'base64'));
  console.log('Captured '+cards.length+' trail illustrations.');
}finally{await browser.close();}
