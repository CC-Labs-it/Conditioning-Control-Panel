/** Preserve the displayed text for saves, accessibility and other score effects. Only its last digit wears EMI. */
export function writeScore(node, text, wearingEmi = false) {
  const digit = text.slice(-1);
  if (!wearingEmi || !/[0-9]/.test(digit)) { node.textContent = text; return; }
  let mini = node.lastElementChild;
  if (!mini?.hasAttribute('data-et-digit')) {
    mini = node.ownerDocument.createElement('span');
    mini.setAttribute('data-et-digit', '');
    mini.setAttribute('data-et-pet', '');
    node.replaceChildren(node.ownerDocument.createTextNode(''), mini);
  }
  node.firstChild.data = text.slice(0, -1);
  if (mini.dataset.etDigit !== digit) {
    mini.dataset.etDigit = digit;
    mini.textContent = digit;
  }
}
