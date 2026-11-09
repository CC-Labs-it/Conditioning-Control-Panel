/* The finished Drone run keeps its route, tabs and renewal actions on one instrument panel. */
import { ENDINGS } from '../data/content/endings.js';
import { iconFor } from './icons.js';
import { mountRunPoster } from './adcard/recap.js';

const STAT_ICONS = { Time: 'tr_skin', Unlocks: 'lo_key', 'Endings seen': 'z_ends' };

export function openUnitRecap({ cards, doc, slot, stats, buttons, line, run, life, name }) {
  function make(tag, cls, text) {
    const el = doc.createElement(tag);
    if (cls) el.className = cls;
    if (text != null) el.textContent = text;
    return el;
  }
  const layout = make('div', 'ur-layout');
  const report = make('div', 'ur-report');
  const summary = make('div', 'ur-summary');
  const header = make('header', 'ur-head');
  const lens = make('div', 'ur-lens');
  lens.setAttribute('aria-hidden', 'true');
  lens.append(make('i'));
  const heading = make('h2', '', ENDINGS.dr_end.title);
  heading.id = 'unit-recap-title';
  const type = make('div', 'ur-type', 'Drone ending');
  const words = make('div', 'ur-words');
  words.append(type, heading, make('p', '', line));
  header.append(words, lens);

  const route = make('div', 'ur-route');
  const routeList = make('ol');
  routeList.setAttribute('aria-label', 'Route');
  for (const pick of stats.Route.split(', ')) routeList.append(make('li', '', pick));
  route.append(make('span', 'ur-kicker', 'Route'), routeList);
  const metrics = make('dl', 'ur-metrics');
  for (const [label, icon] of Object.entries(STAT_ICONS)) {
    const metric = make('div'), term = make('dt', '', label);
    metric.append(term, make('dd', '', stats[label]));
    term.prepend(iconFor(doc, { id: icon }));
    metrics.append(metric);
  }
  summary.append(header, route, metrics);

  const history = make('div', 'ur-history');
  history.append(slot);
  report.append(summary, history);
  const poster = make('aside', 'unit-poster');
  poster.setAttribute('aria-label', 'Run poster');
  layout.append(report, poster);
  let unmount;
  cards.open({
    node: layout, className: 'credits unit-recap', clear: true, guard: false, buttons,
    onOpen(backdrop) {
      backdrop.classList.add('ur-backdrop');
      const card = backdrop.querySelector('.unit-recap');
      card.setAttribute('aria-labelledby', heading.id);
      card.tabIndex = -1;
      const actions = card.querySelector('.card-buttons');
      actions.firstElementChild.prepend(iconFor(doc, { id: 'sparkle' }));
      history.append(actions);
      if (!card.closest('[inert]')) card.focus({ preventScroll: true });
      unmount = mountRunPoster({ doc, host: poster, run, life, name });
    },
    onClose() { unmount?.(); },
  });
}
