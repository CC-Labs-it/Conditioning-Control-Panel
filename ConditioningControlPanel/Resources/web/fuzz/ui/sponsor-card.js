import { drawFace, FACE_CSS } from './unlocks/extras/emi-face.js';

const poses = ['body-idle', 'body-smug', 'body-idle', 'body-smug'];
const expressions = ['^_^', '¬_¬', 'o_o', '-_-'];
const style = `
${FACE_CSS}
.tc-sponsor-card {
  display: grid; grid-template-columns: 32% minmax(0, 1fr); width: min(318px, calc(100% - 20px));
  height: 112px; box-sizing: border-box; overflow: hidden; isolation: isolate; pointer-events: none;
  border: 2px solid #21162e; border-radius: 9px 9px 13px 9px; background: #ffefd9; color: #24172d;
  box-shadow: 0 3px 0 #20142e, 0 6px 15px #100c234d; text-align: left; text-shadow: none;
  font: 600 14px/1 "Segoe UI", Arial, sans-serif;
}
.tc-sponsor-booth {
  position: relative; overflow: hidden; border-right: 2px solid #281b37;
  background:
    radial-gradient(ellipse at 66% 64%, transparent 38%, #d8c9fd54 39% 40%, transparent 41% 52%, #bdece955 53% 54%, transparent 55%),
    radial-gradient(ellipse at 52% 66%, #e883ca 0%, #9178bd 40%, #49426e 71%, #25243f 100%);
}
.tc-sponsor-booth::before {
  content: ""; position: absolute; inset: 0; opacity: .23;
  background: radial-gradient(circle, #fff 0 .7px, transparent .95px) 0 0 / 4px 4px;
}
.tc-sponsor-booth::after {
  content: ""; position: absolute; inset: auto 0 0; height: 4px;
  background: linear-gradient(90deg, #e6f4ca 0 20%, #eac55b 20% 40%, #51c9ca 40% 60%, #ee81b1 60% 80%, #897bc5 80%);
}
.tc-sponsor-tally {
  position: absolute; z-index: 1; left: 5px; top: 5px; padding: 3px 5px;
  display: flex; align-items: center; gap: 4px; border-radius: 2px; background: #211b30; color: #fff4ea;
  font: 800 9px/1 Consolas, "Courier New", monospace; letter-spacing: .8px;
}
.tc-sponsor-tally::before { content: ""; width: 5px; height: 5px; border-radius: 50%; background: #ff809b; box-shadow: 0 0 4px #ff809b; }
.tc-sponsor-portrait { position: absolute; left: 50%; bottom: 5px; width: min(98%, 87px); aspect-ratio: 1; transform: translateX(-50%); }
.tc-sponsor-portrait > img { display: block; width: 100%; height: 100%; object-fit: contain; filter: drop-shadow(0 2px 1px #33233e88); }
.tc-sponsor-script {
  position: relative; display: grid; grid-template-rows: 26px 1fr 4px; gap: 2px;
  padding: 6px 11px 8px; min-width: 0; box-sizing: border-box;
  background: linear-gradient(125deg, #fff9eb, #fff0d9 76%, #f6dfcb);
}
.tc-sponsor-id { display: flex; align-items: center; justify-content: space-between; gap: 4px; border-bottom: 1px solid #baa69d; }
.tc-sponsor-name {
  color: #6d254b; font: italic 900 21px/.95 "Arial Black", "Segoe UI", Arial, sans-serif; letter-spacing: -1.6px;
}
.tc-sponsor-disclosure { color: #5e424d; font: 700 9px/1 Consolas, "Courier New", monospace; letter-spacing: .7px; }
.tc-sponsor-line { align-self: center; margin: 0; color: #261c32; font: 750 19px/1.09 "Segoe UI", Arial, sans-serif; overflow-wrap: anywhere; }
.tc-sponsor-cues { display: flex; gap: 3px; height: 3px; align-self: end; }
.tc-sponsor-cues > span { flex: 1; background: #d9c7bc; }
.tc-sponsor-cues > span[data-lit] { background: #ba3b77; }
`;

export function createSponsorCard(root, cueCount) {
  const doc = root.ownerDocument;
  if (!doc.getElementById('tc-sponsor-card-style')) {
    const sheet = doc.createElement('style');
    sheet.id = 'tc-sponsor-card-style';
    sheet.textContent = style;
    doc.head.append(sheet);
  }
  root.classList.add('tc-sponsor-card');
  root.setAttribute('role', 'group');
  root.setAttribute('aria-label', 'EMI sponsored message');
  root.innerHTML = `<div class="tc-sponsor-booth" aria-hidden="true">
    <span class="tc-sponsor-tally">ON AIR</span>
    <div class="tc-sponsor-portrait"><img alt="" draggable="false"></div>
  </div>
  <div class="tc-sponsor-script">
    <div class="tc-sponsor-id"><b class="tc-sponsor-name">EMI</b><span class="tc-sponsor-disclosure">SPONSORED</span></div>
    <p class="tc-sponsor-line"></p>
    <div class="tc-sponsor-cues" aria-hidden="true"></div>
  </div>`;
  const portrait = root.querySelector('.tc-sponsor-portrait');
  const body = portrait.querySelector('img');
  const face = drawFace(portrait, 'happy');
  const line = root.querySelector('.tc-sponsor-line');
  const track = root.querySelector('.tc-sponsor-cues');
  const cues = Array.from({ length: cueCount }, () => track.appendChild(doc.createElement('span')));
  return {
    show(text, index) {
      const beat = index % poses.length;
      body.src = new URL('../assets/img/emi/' + poses[beat] + '.png', import.meta.url).href;
      face.paint(expressions[beat]);
      line.textContent = text;
      cues.forEach((cue, i) => cue.toggleAttribute('data-lit', i <= index));
    },
  };
}
