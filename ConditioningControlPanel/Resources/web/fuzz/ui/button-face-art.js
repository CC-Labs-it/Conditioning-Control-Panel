// Fixed artwork for the game button and its shop samples. Player text is assigned through textContent.
export const FACE_HTML = Object.freeze({
  'bi-lips': `<span class="tc-login"><span class="tc-cloud" aria-hidden="true"><i>♥</i></span><b>Sign in with Bambi Cloud</b></span>`,
  'dr-hex': `<span class="tc-download"><span class="tc-dlarrow" aria-hidden="true">⬇</span><span class="tc-dltype"><b>DOWNLOAD NOW</b><small>drone_firmware.exe</small></span><span class="tc-meter" aria-hidden="true"></span></span>`,
  'human-check': `<span class="tc-check"><span class="tc-box" aria-hidden="true"></span><b>Verify you are human</b><span class="tc-seal" aria-hidden="true"><i>↻</i><small>reFUZZ</small></span></span>`,
  'tr-pendulum': `<span class="tc-oracle">
          <span class="tc-theatre" aria-hidden="true"><span class="tc-dial"></span><span class="tc-notch"></span><span class="tc-pend"><span class="tc-chain"></span><span class="tc-ball"><span class="tc-eight">8</span><span class="tc-window"><i></i></span></span></span><span class="tc-pin"></span></span>
          <span class="tc-oracle-lettering"><small>FORTUNE TELLER</small><strong>Will I go<br><em>deeper?</em></strong><span class="tc-readout">Tap on center</span></span>
        </span>`,
  'padlock': `<span class="tc-recover"><span class="tc-key" aria-hidden="true"><i></i></span><span class="tc-mail"><small>KEYHOLDER SUPPORT</small><b>Forgot your key?</b></span></span>`,
  'censorbar': `<span class="tc-spoiler"><span class="tc-bars" aria-hidden="true"></span><small>REDACTED</small><b>Show sensitive content</b></span>`,
  'si-parcel': `<span class="tc-parcel"><span class="tc-pc-top"><span class="tc-pc-icon" aria-hidden="true"></span><span class="tc-pc-copy"><small>ORDER #1104 · TRACKING</small><b>Packed</b></span></span><span class="tc-pc-track" aria-hidden="true"><i class="on"></i><i></i><i></i><i></i><i></i></span><small class="tc-pc-item">1x frilly uniform · size: yours</small></span>`,
  /* EMI: the pixel body plus her screen face (drawn by emi-face.js drawFace into .tc-emi-art); the tag is the press hint. */
  'emi': `<span class="tc-emi"><span class="tc-emi-art"><img class="tc-emi-body" src="assets/img/emi/body-idle.png" alt="" draggable="false"></span><small class="tc-emi-tag">Pet me</small></span>`,
  'worn': `<span class="tc-playerbox"><b>Are you still watching?</b><span class="tc-playbtn"><i aria-hidden="true"></i><span>Continue watching</span></span><span class="tc-time" aria-hidden="true"></span></span>`,
});
