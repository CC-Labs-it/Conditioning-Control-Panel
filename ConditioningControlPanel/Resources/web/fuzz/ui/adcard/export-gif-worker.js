import { createGifEncoder } from './export-gif.js';

const encoder = createGifEncoder();
self.onmessage = ({ data }) => {
  try {
    if (data.finish) {
      const bytes = encoder.finish();
      self.postMessage({ bytes }, [bytes.buffer]);
    } else {
      encoder.frame(data);
      self.postMessage({ ready: true });
    }
  } catch (error) { self.postMessage({ error: error.message }); }
};
self.postMessage({ ready: true });
