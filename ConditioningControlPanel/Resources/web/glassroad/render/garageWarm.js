/** Stage every wardrobe variant together behind the cover. One bounds walk and one
 * compiler traversal prepare the same materials without rebuilding the whole garage
 * for each outfit. Temporary skeleton bindings never survive this operation. */
export async function warmGarageWardrobe({ outfits, attach, measure, compile, restore }) {
  const bindings = [];
  let failed = false;
  try {
    for (const outfit of outfits) bindings.push(attach(outfit));
    const bounds = measure();
    await compile();
    return bounds;
  } catch (error) {
    failed = true; throw error;
  } finally {
    let cleanupError;
    for (const binding of bindings) {
      try { binding?.dispose(); } catch (error) { cleanupError ||= error; }
    }
    try { restore(); } catch (error) { cleanupError ||= error; }
    if (!failed && cleanupError) throw cleanupError;
  }
}
