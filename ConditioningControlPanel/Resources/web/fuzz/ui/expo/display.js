// Paint variable exhibit records onto the authored placard UVs.
const PAPER = '#efe9d5', INK = '#123e34', GOLD = '#a27b45';
export function livePanel(THREE, mesh, {width = 1280,height = 600, emission = 0} = {}) {
  const canvas = document.createElement('canvas');
  canvas.width = width; canvas.height = height;
  const g = canvas.getContext('2d');
  const texture = new THREE.CanvasTexture(canvas);
  texture.colorSpace = THREE.SRGBColorSpace;
  texture.flipY = false;
  texture.anisotropy = 8;
  const surface = mesh.material.clone();
  const original = mesh.material;
  surface.map = texture;
  surface.color?.set(0xffffff);
  surface.emissiveMap = emission > 0 ? texture : null;
  surface.emissive?.set(emission > 0 ? 0xffffff : 0x000000);
  surface.emissiveIntensity = emission;
  mesh.material = surface;
  function draw(fn) {
    g.save(); g.fillStyle = PAPER; g.fillRect(0,0,width,height);
    fn(g,width,height); g.restore(); texture.needsUpdate = true;
  }
  return {draw, dispose() { mesh.material = original; surface.dispose(); texture.dispose(); }};
}

function linesOf(g, text, width) {
  const result = [];
  for (const paragraph of String(text).split('\n')) {
    let line = '';
    for (const word of paragraph.split(/\s+/)) {
      const next = line ? line + ' ' + word : word;
      if (line && g.measureText(next).width > width) { result.push(line); line = word; }
      else line = next;
    }
    result.push(line);
  }
  return result;
}

export function placard(panel, caption, body, foot = '') {
  panel?.draw((g,w,h) => {
    g.strokeStyle = GOLD; g.lineWidth = 4; g.strokeRect(17,17,w-34,h-34);
    g.fillStyle = INK; g.textBaseline = 'top'; g.font = 'bold '+Math.round(h*.075)+'px "Segoe UI",sans-serif';
    g.fillText(caption.toUpperCase(), w*.047,h*.085,w*.906);
    g.fillStyle = GOLD; g.fillRect(w*.047,h*.195,w*.906,3);
    let size = Math.round(h*.214), lines;
    for (; size >= 18; size -= 2) {
      g.font = size+'px Georgia,serif'; lines = linesOf(g,body,w*.906);
      if (lines.length * size * 1.19 <= h*(foot ? .56 : .67)) break;
    }
    g.fillStyle = INK;
    lines.forEach((line,i) => g.fillText(line,w*.047,h*.247+i*size*1.19,w*.906));
    if (foot) {
      g.font = '600 '+Math.round(h*.074)+'px "Segoe UI",sans-serif';
      g.fillStyle = '#466153'; g.fillText(foot,w*.047,h*.846,w*.906);
    }
  });
}

export function terminal(panel,{title,rows,foot,good=false,pictureGrid=false,eyebrow='HUMAN INTERFACE PROGRAMME'}) {
  panel?.draw((g,w,h) => {
    g.fillStyle = '#123f38'; g.fillRect(0,0,w,h);
    if(pictureGrid){
      // The printed tile in front of the monitor carries the title and nine photographs.
      g.fillStyle='#f1f0d9';g.textAlign='center';g.font='bold 57px "Segoe UI",sans-serif';
      g.fillText(rows[0],w/2,h*.128,w*.91);
      g.fillStyle='#6c8d73';g.fillRect(w*.045,h*.166,w*.91,3);
      return;
    }
    g.fillStyle = '#d2e8b3'; g.font = 'bold 45px "Segoe UI",sans-serif'; g.fillText(eyebrow,48,69,w-96);
    g.strokeStyle = '#6c8d77'; g.beginPath(); g.moveTo(50,100); g.lineTo(w-50,100); g.stroke();
    g.fillStyle = '#f8f1d8'; g.font = 'bold 80px Georgia,serif'; g.fillText(title,50,197,w-100);
    const rowHeight=Math.min(88,Math.floor((h-294)/Math.max(1,rows.length)));
    g.font = Math.min(64,rowHeight-12)+'px Consolas,monospace';
    rows.forEach((line,i) => g.fillText(line,50,282+i*rowHeight,w-100));
    g.fillStyle = good ? '#e2ee9b' : '#d8e5d3'; g.font = '42px "Segoe UI",sans-serif';
    if(foot) g.fillText(foot,50,h-38,w-100);
  });
}

export function runMonitor(panel,{name,presses,yes,time,pressed}) {
  panel?.draw((g,w,h)=>{
    g.fillStyle='#173a36';g.fillRect(0,0,w,96);
    g.fillStyle='#f3efd8';g.font='bold 57px Georgia,serif';g.fillText('Terms & Conditioning',36,68,w*.72);
    g.font='38px Consolas,monospace';g.textAlign='right';g.fillText(time,w-36,66,w*.23);
    g.textAlign='left';g.fillStyle=INK;g.font='58px "Segoe UI",sans-serif';g.fillText(name,42,166,w-84);
    g.fillStyle='#425747';g.font='34px "Segoe UI",sans-serif';g.fillText('VISITOR RECORD',43,219);
    g.fillStyle='#345146';g.fillRect(43,244,550,2);g.fillRect(628,244,w-676,2);
    g.font='bold 80px Georgia,serif';g.fillStyle=INK;g.fillText(yes.toLocaleString()+' Yes',42,352,540);
    g.font='51px "Segoe UI",sans-serif';g.fillText(presses.toLocaleString()+' presses',42,436,550);
    const y=pressed?283:273;
    g.fillStyle='#102e27';g.beginPath();g.roundRect(628,287,w-675,119,12);g.fill();
    g.fillStyle=pressed?'#3e6653':'#517b61';g.beginPath();g.roundRect(628,y,w-675,119,12);g.fill();
    g.fillStyle='#fff9e5';g.textAlign='center';g.font='bold 69px "Segoe UI",sans-serif';g.fillText('I AGREE',(w+581)/2,y+84,w-715);
    g.textAlign='left';g.fillStyle='#4b5c49';g.font='38px "Segoe UI",sans-serif';g.fillText('LIVE   /   Human Habitat',42,h-36);
  });
}

