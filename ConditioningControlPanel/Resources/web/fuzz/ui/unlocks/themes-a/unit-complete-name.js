/* The signed-in tag is truncated in the HUD and the Drone plate can sit outside the phone's meter strip.
 * Bring its full label above the strips for the last shot. Both cuts use this same piece of lettering. */
export function nameTag(ctx) {
  const doc = ctx.world.ownerDocument;
  const account = doc.getElementById('account-chip'), serial = doc.querySelector('#l-meters > [data-fz-owner="dr_serial"]');
  const displayName = ctx.identity?.displayName?.trim();
  const seed = Number.isFinite(ctx.state?.seed) ? ctx.state.seed : 0;
  const number = ctx.state?.store?.dr_serial?.serial || 1000 + seed % 9000;
  return displayName ? { source: account || serial, label: displayName } : { source: serial, label: 'UNIT ' + number };
}

export function makeName(ctx, stage, canvas, { source, label }) {
  const doc = stage.ownerDocument, tag = doc.createElement('div');
  tag.dataset.unitName = ''; tag.hidden = true; tag.setAttribute('aria-hidden', 'true');
  stage.insertBefore(tag, canvas);
  const makeLayer = cls => {
    const layer = doc.createElement('div'); layer.className = cls;
    const text = doc.createElement('span'); text.textContent = label; text.dir = 'auto';
    layer.append(text); tag.append(layer); return layer;
  };
  const ghost = makeLayer('uc-name-ghost'), face = makeLayer('uc-name-face');
  const staticStrip = doc.createElement('canvas');
  staticStrip.dataset.unitStatic = ''; staticStrip.hidden = true; staticStrip.setAttribute('aria-hidden', 'true');
  staticStrip.width = 224; staticStrip.height = 10; stage.append(staticStrip);
  // One strip of phosphor noise. The raster only moves a few pixels and never illuminates the whole screen.
  const pen = staticStrip.getContext('2d');
  if (pen) {
    const noise = pen.createImageData(staticStrip.width, staticStrip.height);
    let seed = 81347;
    for (let y = 0; y < staticStrip.height; y++) for (let x = 0; x < staticStrip.width; x++) {
      seed = (Math.imul(1664525, seed) + 1013904223) >>> 0;
      const grain = seed / 0x100000000, line = y === 4 ? .9 : y === 3 || y === 6 ? .52 : .17;
      const edge = Math.min(1, x / 38, (223 - x) / 38), i = (y * staticStrip.width + x) * 4;
      noise.data[i] = 52 + grain * 88; noise.data[i + 1] = 148 + grain * 89; noise.data[i + 2] = 100 + grain * 66;
      noise.data[i + 3] = 255 * line * edge * (grain < .29 ? 0 : grain);
    }
    pen.putImageData(noise, 0, 0);
  }
  const css = `
  [data-unit-name]{position:absolute;display:grid;width:max-content;max-width:min(84vw,650px);pointer-events:none;
    font:700 clamp(19px,3.5vw,32px)/1.16 Consolas,'Courier New',monospace;text-align:center;letter-spacing:.065em;
    overflow-wrap:anywhere;transform:translate(-50%,-50%);isolation:isolate}
  [data-unit-name][hidden]{display:none}
  [data-unit-name]>div{grid-area:1/1;position:relative;border:1px solid;border-radius:5px;padding:.5em 1.1em;min-width:min(10em,75vw);box-sizing:border-box}
  [data-unit-name]>div::before,[data-unit-name]>div::after{content:'';position:absolute;top:22%;bottom:22%;width:3px;
    background:repeating-linear-gradient(0deg,currentColor 0 2px,transparent 2px 4px);opacity:.72}
  [data-unit-name]>div::before{left:8px}[data-unit-name]>div::after{right:8px}
  [data-unit-name] .uc-name-face{color:#edfff5;border-color:#86b5a0;background:linear-gradient(165deg,#789b8d 0,#3b524c 7%,#243a32 35%,#10271e 63%,#253d32 92%,#719687);
    box-shadow:inset 0 2px #d6f6df88,inset 0 -2px #07130d;text-shadow:0 1px 2px #000,0 0 9px #88dda466}
  [data-unit-name] .uc-name-ghost{color:#85e7aa;border-color:#44845e;background:linear-gradient(0deg,#0a1910,#101d17);
    box-shadow:inset 0 0 0 2px #10281b}
  [data-unit-name] .uc-name-ghost span{color:#0c1b12;-webkit-text-stroke:1px #87d9a3;text-shadow:0 0 4px #3b985f66}
  [data-unit-name-source][data-unit-lifted]{opacity:0!important;pointer-events:none!important}
  [data-unit-static]{position:absolute;pointer-events:none;image-rendering:pixelated;max-width:82vw;height:11px}
  `;
  ctx.css(css);
  if (source) source.dataset.unitNameSource = '';
  const letters = Array.from(label);
  let launch = null;
  function show(hold, first, second, origin, width, height, soft) {
    if (tag.hidden) {
      const r = source?.getBoundingClientRect(), crop = source?.closest('#l-meters')?.getBoundingClientRect();
      if (r?.width && r.height && r.top >= 0 && r.bottom <= height && (!crop || r.bottom >= crop.top && r.top < crop.bottom)) launch = r;
      if (source) source.dataset.unitLifted = '';
      tag.hidden = false;
    }
    const t = Math.min(1, hold / .31), settle = soft ? 1 : 1 - (1 - t) ** 3;
    const x = width * .5, y = height * (origin.y < height * .48 ? .61 : .32);
    const startX = launch ? launch.left + launch.width / 2 : x, startY = launch ? launch.top + launch.height / 2 : y;
    const scale = soft || !launch ? 1 : .64 + .36 * settle;
    tag.style.left = (startX + (x - startX) * settle) + 'px'; tag.style.top = (startY + (y - startY) * settle) + 'px';
    tag.style.transform = `translate(-50%,-50%) scale(${scale})`;
    tag.style.opacity = String(launch || soft ? 1 : Math.min(1, hold / .17));
    if (soft) {
      face.style.clipPath = ghost.style.clipPath = '';
      face.style.opacity = 1 - first; ghost.style.opacity = 1 - second;
    } else {
      face.style.opacity = ghost.style.opacity = '1';
      face.style.clipPath = `inset(0 0 0 ${first * 100}%)`;
      ghost.style.clipPath = `inset(0 ${second * 100}% 0 0)`;
    }
    return tag.getBoundingClientRect();
  }
  function clear() { if (source) { delete source.dataset.unitNameSource; delete source.dataset.unitLifted; } }
  function tracking(rect, phase) {
    staticStrip.hidden = !(phase >= 0 && phase < 1);
    if (staticStrip.hidden) return;
    const u = Math.sin(Math.PI * phase);
    staticStrip.style.width = Math.min(480, rect.width * 1.22) + 'px';
    staticStrip.style.left = rect.left + rect.width / 2 + 'px';
    staticStrip.style.top = rect.top + rect.height * .53 + 'px';
    staticStrip.style.transform = `translate(-50%,-50%) translateX(${Math.round(5 * u * Math.sin(phase * 15))}px) scaleX(${.89 + .11 * u})`;
    staticStrip.style.opacity = String(.28 + .62 * u);
  }
  return { tag, letters, show, tracking, clear };
}
