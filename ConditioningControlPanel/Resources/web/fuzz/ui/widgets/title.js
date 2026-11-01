/* Window Title (5): the tab reads "Untitled" until bought. "Good for privacy." */
export const TITLE = 'Quarterly Report.xlsx';
export const title = {
  mounted: false,
  mount(app) { app.doc.title = TITLE; this.mounted = true; },
};
