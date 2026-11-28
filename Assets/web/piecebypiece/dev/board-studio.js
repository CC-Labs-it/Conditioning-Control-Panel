const frame = document.getElementById('game');
const boardChoice = document.getElementById('board');
const finishChoice = document.getElementById('finish');
const emi = document.getElementById('emi');
const status = document.getElementById('status');
let state = null, ply = 0;
const moves = ['e2e4','e7e5','g1f3','b8c6','f1c4','g8f6','d2d3','f8c5','c2c3','d7d6','b1d2','c8g4','h2h3','g4h5','d1b3','d8d7','b3b7','a8b8'];

function apply() {
  if (!state) return;
  state.wear.choose('board', boardChoice.value ? 'chess.board.'+boardChoice.value : null);
  state.wear.choose('finish', finishChoice.value ? 'chess.finish.'+finishChoice.value : null);
  state.wear.choose('companion', emi.checked ? 'chess.companion.emi' : null);
}
for (const field of [boardChoice,finishChoice,emi]) field.addEventListener('change', apply);
function next() {
  if (!state || state.isPaused()) return null;
  const encoded = moves[ply];
  const move = encoded && state.game.tryMove(encoded.slice(0,2), encoded.slice(2));
  if (!move) { status.textContent = 'Continue playing on the board, or start a new game.'; return null; }
  ply++;
  status.textContent = move.san || (encoded.slice(0,2)+' to '+encoded.slice(2));
  return move;
}
document.getElementById('move').onclick = next;
document.getElementById('reset').onclick = () => {
  if (!state) return;
  state.pause(false);
  state.game.reset();
  ply=0;
  document.getElementById('pause').textContent='Pause';
  status.textContent='Test cosmetics are equipped. Play a move or use the sequence.';
};
document.getElementById('pause').onclick = event => {
  if (state) { state.pause(!state.isPaused()); event.target.textContent = state.isPaused() ? 'Resume' : 'Pause'; }
};
const base = new URL('../',location.href);
const response = await fetch(new URL('index.html',base));
if (!response.ok) throw Error('The chess page could not load.');
let html = await response.text();
html = html.replace('<head>', '<head><base href="'+base.href+'"><link rel="icon" href="data:,"><script src="./dev/board-host.js"></script>');
frame.srcdoc = html;
const ready = setInterval(async () => {
  const win=frame.contentWindow, pbp=win?.PBP;
  if (!pbp?.door || !pbp.board.theme || !pbp.board.themeEffects || !pbp.board.cheerleaders) return;
  clearInterval(ready);
  state=pbp;
  const prefs=await win.eval('import('+JSON.stringify(new URL('game/preferences.js',base).href)+')');
  prefs.setPresentation({ experience:'classic',boardIntro:false,nameTags:false,followCam:false,replays:false,openIntro:false,volume:0 });
  win.document.querySelector('[data-act="hotseat"]')?.click();
  state.camera.set({theta:.20,phi:.80,radius:16.8,target:[0,.12,0]},true);
  apply();
  status.textContent='Test cosmetics are equipped. Play a move or use the sequence.';
  window.boardStudio={state,apply,next,frame};
},120);
