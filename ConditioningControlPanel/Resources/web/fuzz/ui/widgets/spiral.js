/* Spiral (100), Spiral Speed (500 x1.3) and Spiral Overlay (50,000) all drive
 * the one Loom canvas in fx/spiral.js. */
export const spiralWidgets = {
  spiral: {
    mounted: false,
    mount(app) { app.spiral?.show(); this.mounted = true; },
  },
  speed: {
    mounted: false,
    mount(app, r) { app.spiral?.faster(1 + 0.15 * (r?.count || 1)); this.mounted = true; },
  },
  overlay: {
    mounted: false,
    mount(app) { app.doc.getElementById('spiral').style.zIndex = '15'; app.doc.getElementById('spiral').style.opacity = '0.3'; this.mounted = true; },
  },
};
