import { emiTileSprite } from '../../emi-tile-sprite.js';
import { writeScore } from '../../emi-score.js';

export const UI_TAKEOVER_CSS = `
[data-et-ui] > [data-et-original]{display:none!important}
.et-ui-emi,#account-chip .et-ui-emi{display:inline-block;position:relative;width:32px;height:32px;flex:0 0 auto;pointer-events:none}
.et-ui-emi>svg{display:block;width:100%;height:100%}
#shop .et-ui-emi{width:44px;height:44px;max-width:100%}
#goal .et-ui-emi{width:52px;height:52px}
#account-chip .et-ui-emi{width:26px;height:26px}
#account-chip[data-et-ui]{color:#ffd2e5;background:#392c40}
#account-chip[data-et-ui] .et-name{display:inline;color:inherit}
[data-et-digit]{display:inline-block;position:relative;width:.90em;height:.93em;vertical-align:-.14em;color:transparent!important;text-shadow:none!important;-webkit-text-stroke:0 transparent!important;background:url("assets/img/emi/body-idle.png") center/contain no-repeat;image-rendering:pixelated;text-align:center}
[data-et-digit]::after{content:attr(data-et-digit);position:absolute;left:30%;top:27%;width:48%;height:38%;display:grid;place-content:center;font:800 .33em/1 system-ui,sans-serif;letter-spacing:0;color:#ffb3d9}
body.tb-hidecount:not(.tb-peek) [data-et-digit]{background:none;color:inherit!important}body.tb-hidecount:not(.tb-peek) [data-et-digit]::after{display:none}
@media(max-width:560px){#account-chip[data-et-ui] .et-name{display:none}}
`;

/** The host controls keep their own buttons, labels, prices and input listeners as the takeover changes their art. */
export function createUiTakeover(ctx) {
  const doc = ctx.button.el.ownerDocument, counter = ctx.parts.counter;
  const targets = new Map();
  let beat = Math.max(0, Math.min(9, Number(ctx.store.get('uiBeat')) || 0));
  let clock = 0, active = false;

  function wear(button, original, account = false) {
    if (!button || targets.has(button)) return;
    const mini = doc.createElement('span');
    mini.className = 'et-ui-emi'; mini.setAttribute('aria-hidden', 'true'); mini.dataset.etPet = '';
    mini.innerHTML = emiTileSprite();
    original?.setAttribute('data-et-original', '');
    button.insertBefore(mini, original || null);
    let name = null;
    if (account) {
      name = doc.createElement('span');
      name.className = 'et-name'; name.textContent = 'EMI';
      button.appendChild(name);
    }
    button.dataset.etUi = '';
    targets.set(button, () => {
      delete button.dataset.etUi;
      original?.removeAttribute('data-et-original');
      mini.remove(); name?.remove();
    });
  }

  function digit(on) {
    if (counter.hasAttribute('data-et-score') === on) return;
    counter.toggleAttribute('data-et-score', on);
    const plain = doc.getElementById('count');
    const roll = doc.querySelector('#fz-roll > .d:last-child');
    for (const node of [plain, roll]) if (node) writeScore(node, node.textContent, on);
  }
  function clear() {
    if (!active) return;
    active = false;
    for (const undo of targets.values()) undo();
    targets.clear();
    digit(false);
  }
  function update(seconds, hive) {
    if (!hive) { clear(); return; }
    active = true;
    clock += seconds;
    if (clock >= 2.5) {
      clock %= 2.5;
      if (beat < 9) { beat++; ctx.store.set('uiBeat', beat); }
    }
    if (beat === 0) return;
    for (const [button,undo] of targets) if (!button.isConnected) { undo(); targets.delete(button); }
    const rows = doc.querySelectorAll('#shop .row[data-id]');
    const take = Math.max(1,beat-2);
    for (let i=0; i<Math.min(rows.length,take); i++) {
      const row = rows[i];
      wear(row, row.querySelector(':scope > .rep-ico, :scope > .ico'));
    }
    if (beat >= 2) {
      const panel = doc.getElementById('ctl-panel-btn');
      if (panel) wear(panel, panel.firstElementChild);
    }
    if (beat >= 3) digit(true);
    if (beat >= 4) {
      const account = doc.getElementById('account-chip');
      if (account) wear(account, account.querySelector('span'), true);
    }
  }
  ctx.cleanup(clear);
  return { update };
}
