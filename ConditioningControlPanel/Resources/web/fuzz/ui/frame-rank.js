// Picture and caption frames have no regular collect action of their own.
export function addFrameCollection(ctx, body) {
  const journey = ctx.state.store.journey;
  if (!journey) return;
  const id = ctx.item.id;
  const button = ctx.el('button', 'position:absolute;bottom:5px;left:5px;z-index:5;', 'Collect', body);
  button.type = 'button';
  function refresh() {
    button.hidden = !journey.ranks[id];
    const amount = Math.floor((journey.banks[id] || 0) * ctx.state.pace);
    button.disabled = amount < 1;
    const label = amount ? 'Collect +' + ctx.fmt(amount) : 'Collect';
    if (button.textContent !== label) button.textContent = label;
  }
  button.addEventListener('click', (event) => {
    event.stopPropagation();
    if (!ctx.paused && journey.banks[id]) ctx.pay(0, id, {x:event.clientX, y:event.clientY});
    refresh();
  });
  refresh();
  ctx.on('buy', refresh);
  ctx.every(500, refresh);
}
