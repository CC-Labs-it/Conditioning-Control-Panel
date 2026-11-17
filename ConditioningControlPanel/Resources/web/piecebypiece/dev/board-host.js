// Local authoring host. The studio uses an isolated frame and test ownership.
const channel = new EventTarget();
localStorage.setItem('pbp-presentation-v1', JSON.stringify({
  experience: 'classic', boardIntro: false, openIntro: false,
  nameTags: false, followCam: false, replays: false, volume: 0,
}));
window.chessStudioHost = {
  sent: [],
  receive(data) { channel.dispatchEvent(new MessageEvent('message', { data })); },
};
channel.postMessage = message => {
  chessStudioHost.sent.push(message);
  if (message.type === 'ready') {
    chessStudioHost.receive({ type: 'pbp:settings', reducedMotion: false, prizeCounter: false, sfxVolume: 0 });
    const slots = {
      finish: ['polka','marble','contour','opal','kintsugi'],
      board: ['observatory','conservatory','theatre','arcade'],
      companion: ['emi'],
      banner: ['house-pink','crest'],
    };
    chessStudioHost.receive({ type: 'owned', grants: Object.entries(slots).flatMap(([slot,ids]) => ids.map(id=>'cos.chess.'+slot+'.'+id)) });
    chessStudioHost.receive({ type: 'pbp:look', look: {
      board: 'chess.board.observatory', companion: 'chess.companion.emi', banner: 'chess.banner.house-pink',
    } });
  }
};
window.chrome ??= {};
window.chrome.webview = channel;
