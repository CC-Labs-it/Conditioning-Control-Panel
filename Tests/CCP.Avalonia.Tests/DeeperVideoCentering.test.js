// Run with node. Exercise the actual injected centering script with a small DOM.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const source = fs.readFileSync(path.join(__dirname, '../../CCP.Avalonia/Views/Deeper/DeeperPreview.cs'), 'utf8');
const match = source.match(/private const string ScrollOnce\s*=\s*("(?:\\.|[^"\\])*")\s*;/);
assert.ok(match, 'centering script is available');
const script = JSON.parse(match[1]);
function video(width, height) {
    return { width, height, count: 0, getBoundingClientRect() { return this; },
        scrollIntoView() { this.count++; } };
}
function doc(videos, frames = []) {
    return { querySelectorAll(s) { return s === 'video' ? videos : s === 'iframe' ? frames : []; } };
}
if (process.argv[2] !== 'visible') {
    const inside = video(400, 250), nested = video(300, 200);
    const root = doc([], [{ get contentDocument() { throw new Error('cross origin'); } },
        { contentDocument: doc([inside], [{ contentDocument: doc([nested]) }]) }]);
    vm.runInNewContext(script, { document: root, l: video(10, 10) });
    assert.equal(inside.count, 1, 'same-origin frame video is centered without a top-level video');
    assert.equal(nested.count, 1, 'nested same-origin frame video is centered');
    vm.runInNewContext(script, { document: root, l: video(10, 10) });
    assert.equal(inside.count, 1, 'a video is centered once');
}
if (process.argv[2] !== 'frames') {
    const hidden = video(0, 0), root = doc([hidden]);
    vm.runInNewContext(script, { document: root, l: hidden });
    assert.equal(hidden.count, 0, 'hidden video is not scrolled');
    assert.ok(!hidden._ccpScrolled, 'hidden video is not marked');
    hidden.width = 400; hidden.height = 250;
    vm.runInNewContext(script, { document: root, l: hidden });
    assert.equal(hidden.count, 1, 'video is centered when it becomes visible');
}
console.log('Deeper video centering: OK');
