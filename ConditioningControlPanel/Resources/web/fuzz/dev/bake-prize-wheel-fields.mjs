// Bake only the visible materials, with no audio or runtime dependency on Afterimage.
// node dev/bake-prize-wheel-fields.mjs <retained dense-atlas.html> [output.webp]
import { readFile, writeFile } from 'node:fs/promises';
import { createRequire } from 'node:module';
import { fileURLToPath } from 'node:url';
import { FIELD_BY_ID, FIELD_WIDTH, FIELD_HEIGHT, FIELD_COLUMNS, FIELD_FRAMES } from '../assets/prize-wheel/fields.js';

const input = process.argv[2];
if (!input) throw Error('Pass the retained dense-atlas.html as the first argument.');
const html = await readFile(input, 'utf8');
const output = process.argv[3] || fileURLToPath(new URL('../assets/prize-wheel/fields.webp', import.meta.url));
const { chromium } = createRequire('C:/wt-fe/e2e/package.json')('playwright');
const browser = await chromium.launch({ channel: 'chrome', headless: true, args: ['--use-angle=swiftshader', '--enable-unsafe-swiftshader'] });
try {
  const page = await browser.newPage();
  // The atlas registers its painters in inline scripts before its UI boots.
  for (const match of html.matchAll(/<script[^>]*>([\s\S]*?)<\/script>/gi)) {
    if (match[1].includes('window.AF_SCENES') && !match[1].includes('root.__atlas')) {
      await page.addScriptTag({content:match[1]});
    }
  }
  const sheet = await page.evaluate(async ({fields, width, height, cols, count}) => {
    const create = (w,h) => Object.assign(document.createElement('canvas'), {width:w,height:h});
    const atlas = create(width * cols, height * Math.ceil(count / cols));
    const dest = atlas.getContext('2d');
    const full = create(480,300), painter = full.getContext('2d');
    const swatch = create(width, 150), sampler = swatch.getContext('2d',{willReadFrequently:true});
    const offsets = [0.4, 2.4, 3.8, 1.15, 4.7, 5.5];
    for(const [i,field] of fields.entries()) {
      const source = window.AF_SCENES.find(s=>s.id===field.source);
      if(!source) throw Error('Missing atlas field '+field.source);
      const colors = field.color.map(c=>[1,3,5].map(pos=>parseInt(c.slice(pos,pos+2),16)));
      // A continuous return along the original field's own timeline gives
      // the same value and tangent at the seam without crossfading textures.
      let low = 0, span = 1;
      for(let frame=0;frame<count;frame++) {
        const turn=frame/count*Math.PI*2;
        const t=10+i*3.2+4.2*Math.sin(turn+offsets[i])+.65*Math.sin(2*turn-.7+i);
        painter.fillStyle='#070715';painter.fillRect(0,0,full.width,full.height);
        source.draw(painter,full.width,full.height,t,{bass:.5,mid:.5,high:.45,beat:.15,energy:.6},2.1);
        // Frame the active interior of the field at the wedge's scale. Each
        // material retains its own topology, local motion and particle depth.
        const cropW=full.width*.72;
        const cropH=Math.min(full.height*.86,cropW*field.height/width);
        sampler.drawImage(full,(full.width-cropW)*.5,(full.height-cropH)*.5,cropW,cropH,0,0,width,field.height);
        const image=sampler.getImageData(0,0,width,field.height);
        // One fixed colour grade for the entire shot. Per-frame auto exposure
        // would turn slow luminance changes into discontinuities in the loop.
        if(frame===0){
          const hist=[];
          for(let j=0;j<image.data.length;j+=4)
            hist.push(Math.max(image.data[j],image.data[j+1],image.data[j+2])*.58+
              (.299*image.data[j]+.587*image.data[j+1]+.114*image.data[j+2])*.42);
          hist.sort((a,b)=>a-b);
          low = hist[Math.floor(hist.length*.07)];
          span = Math.max(25,hist[Math.floor(hist.length*.975)]-low);
        }
        for(let j=0;j<image.data.length;j+=4){
          const [r,g,b]=[image.data[j],image.data[j+1],image.data[j+2]];
          const light=Math.max(r,g,b)*.58 + (.299*r+.587*g+.114*b)*.42;
          const grade=Math.pow(Math.max(0,Math.min(1,(light-low)/span)),.74)*2.999;
          const stop=Math.floor(grade), mix=grade-stop;
          for(let k=0;k<3;k++)
            image.data[j+k]=Math.round(colors[stop][k]*(1-mix)+colors[stop+1][k]*mix);
        }
        dest.putImageData(image,(frame%cols)*width,Math.floor(frame/cols)*height+field.top);
      }
      await new Promise(resolve=>setTimeout(resolve,0));
    }
    // .35: the wheel draws each swatch at about 2x with smoothing, so the
    // signed-off look holds (SSIM .958 vs .79) at 0.72 MB instead of 1.26 MB.
    return atlas.toDataURL('image/webp',.35).split(',')[1];
  }, {fields:[...FIELD_BY_ID.values()],width:FIELD_WIDTH,height:FIELD_HEIGHT,cols:FIELD_COLUMNS,count:FIELD_FRAMES});
  await writeFile(output, Buffer.from(sheet,'base64'));
} finally { await browser.close(); }
