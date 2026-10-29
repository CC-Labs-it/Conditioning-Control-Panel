// A tiny fake DOM for the HUD tests: enough elements, classes, attributes and focus to draw and press the HUD.
class Node {
  constructor(doc, tag) {
    Object.assign(this, { ownerDocument: doc, tagName: tag.toUpperCase(), children: [], parentNode: null, attrs: {}, dataset: {},
      className: '', textContent: '', innerHTML: '', hidden: false, disabled: false, listeners: {} });
    this.classList = {
      add: (c) => { if (!this.classList.contains(c)) this.className = `${this.className} ${c}`.trim(); },
      remove: (c) => { this.className = this.className.split(' ').filter((x) => x && x !== c).join(' '); },
      contains: (c) => this.className.split(' ').includes(c),
    };
  }
  setAttribute(k, v) { this.attrs[k] = String(v); }
  getAttribute(k) { return this.attrs[k] ?? null; }
  append(...ns) { for (const n of ns) { n.remove(); n.parentNode = this; this.children.push(n); } }
  replaceChildren(...ns) { this.children.forEach((c) => { c.parentNode = null; }); this.children = []; this.append(...ns); }
  replaceWith(n) { const p = this.parentNode, i = p.children.indexOf(this); n.remove(); p.children[i] = n; n.parentNode = p; this.parentNode = null; }
  remove() { if (this.parentNode) { const p = this.parentNode; p.children.splice(p.children.indexOf(this), 1); this.parentNode = null; } }
  contains(n) { for (let o = n; o; o = o.parentNode) if (o === this) return true; return false; }
  all() { return this.children.flatMap((c) => [c, ...c.all()]); }
  querySelectorAll(sel) { return this.all().filter((n) => (sel.startsWith('.') ? n.classList.contains(sel.slice(1)) : n.tagName === sel.toUpperCase())); }
  text() { return [this.textContent, ...this.children.map((c) => c.text())].join(' ').replace(/\s+/g, ' ').trim(); }
  focus() { this.ownerDocument.activeElement = this; }
  blur() { if (this.ownerDocument.activeElement === this) this.ownerDocument.activeElement = this.ownerDocument.body; }
  addEventListener(type, fn) { (this.listeners[type] ||= []).push(fn); }
  dispatch(type, ev) { const e = { stopPropagation() {}, preventDefault() { this.defaultPrevented = true; }, ...ev }; (this.listeners[type] || []).forEach((f) => f(e)); return e; }
  getBoundingClientRect() { return { left: 0, top: 0, width: 100, height: 40 }; }
  get offsetWidth() { return 100; }
  get id() { return this.attrs.id; }
  set id(v) { this.attrs.id = v; }
}
export function fakeDocument() {
  const doc = { createElement: (tag) => new Node(doc, tag) };
  doc.body = new Node(doc, 'body');
  doc.activeElement = doc.body;
  return doc;
}
