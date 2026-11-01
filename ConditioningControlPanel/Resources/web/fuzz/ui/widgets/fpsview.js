/* Fuzz per Second (25): shows the readout. "Now you can measure it." */
export const fpsview = {
  mounted: false,
  mount(app) { app.el.fps.hidden = false; app.drawCounter(); this.mounted = true; },
};
