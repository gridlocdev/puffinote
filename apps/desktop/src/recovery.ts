// Window recovery decisions for the main process. No Electron imports, so the tests can run them in Node.

/**
 * What to do with a window whose renderer crashed or was killed. A close or quit under way is finished (there
 * is no page left to ask about unsaved changes). Otherwise the window would stay blank, so the user is told
 * and the page reloaded; a headless run reloads without a dialog.
 */
export function afterRendererGone(opts: {
  readonly closing: boolean;
  readonly quitting: boolean;
  readonly headless: boolean;
}): 'destroy' | 'ask' | 'reload' {
  if (opts.closing || opts.quitting) return 'destroy';
  return opts.headless ? 'reload' : 'ask';
}
