/* Pink (5): the page tint and the bubble's colour. The first thing the player
 * buys back, so the IN is a slow 1.5 s wash (styles.css .pinkwash). */
export const pink = {
  mounted: false,
  mount(app) { app.doc.body.classList.add('pink'); this.mounted = true; },
  unmount(app) { app.doc.body.classList.remove('pink'); this.mounted = false; },
};
