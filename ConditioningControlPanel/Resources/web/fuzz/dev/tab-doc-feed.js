// Local preview only. The existing host owns sign-in, consent, licensing and metering.
export function createPreviewFeed({ onStatus = () => {} } = {}) {
  const local = ['127.0.0.1', 'localhost'].includes(location.hostname);
  const capability = local ? { origin:'device', consent:false, clips:true, sessionOptIn:true, playWarmup:true } : null;
  let transport = null, opening = null, pictures = null, disposed = false, choice = null, requestId = 0;
  function cancel() {
    requestId++;
    const pending=choice;choice=null;
    pending?.cancel?.();pending?.resolve(false);
    transport?.postMessage({type:'scrolller-release'});
  }
  function receive(event) {
    const frame = event.data;
    if (disposed || !frame || typeof frame !== 'object') return;
    if (frame.type === 'setting' && frame.key === 'media.remoteConsent') {
      pictures?.setRemoteConsent(frame.value, frame.reason);
      if (!frame.value) onStatus(frame.reason === 'sign-in' ? 'Sign in to CCP, then choose Scrolller again.' :
        frame.reason === 'allowance-used' ? 'The current Scrolller allowance has been used.' :
        frame.reason === 'disabled' ? 'Scrolller is disabled in the current desktop profile.' : 'Scrolller connection closed.');
    } else if (frame.type === 'scrolller-feed') {
      pictures?.receiveScrolller(frame);
      if (pictures?.source === 'scrolller') onStatus(frame.rows?.length ? `Scrolller connected: ${frame.rows.length} media items` : 'Scrolller is preparing your approved channels.');
    }
  }
  async function open() {
    if (!local || disposed) throw new Error('A local preview connection is required.');
    if (transport) return transport;
    if (!opening) opening = new Promise((resolve, reject) => {
      if (window.chrome?.webview) { resolve(); return; }
      const script = document.createElement('script');
      script.src = '/__wheel-feed/bridge.js'; script.async = true;
      script.onload = () => { script.remove(); resolve(); };
      script.onerror = () => { script.remove(); reject(new Error('The local Scrolller preview host is unavailable.')); };
      document.head.append(script);
    }).then(() => {
      if (disposed || !window.chrome?.webview) throw new Error('The local Scrolller bridge did not open.');
      transport = window.chrome.webview;
      transport.addEventListener('message', receive);
      return transport;
    }).catch(error => { opening = null; throw error; });
    return opening;
  }
  return {
    capability,
    attach(value) { pictures = value; },
    cancel,
    send(type, fields = {}) { transport?.postMessage({ type, ...fields }); },
    async choose() {
      const current=++requestId;
      onStatus('Connecting to the current CCP media profile...');
      try {
        await open();
        if(disposed||current!==requestId) return false;
        return await new Promise(resolve => {
          const pending={resolve,cancel:null};choice=pending;
          pending.cancel=pictures.chooseScrolller((allowed, reason) => {
            if(choice===pending) choice=null;
            if (allowed) onStatus('Scrolller is preparing your approved channels.');
            else if (reason === 'sign-in') onStatus('Sign in to CCP, then choose Scrolller again.');
            resolve(allowed);
          });
        });
      } catch {
        onStatus('Open this preview through its local Scrolller adapter, then try again.');
        return false;
      }
    },
    dispose() {
      if (disposed) return;
      cancel();
      pictures?.setGameplayActive(false);
      transport?.removeEventListener('message', receive);
      disposed = true;
    },
  };
}
