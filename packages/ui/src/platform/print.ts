/** The hidden frame the last print came from; removed when its dialog closes or the next print starts. */
let lastFrame: HTMLIFrameElement | null = null;

/**
 * Print a standalone HTML document from a hidden frame (PRD §10.5): the browser's print dialog, where the
 * user can pick Save as PDF. Only the document prints, none of the app around it. The frame is sandboxed
 * (no scripts run in it), and resolves once the dialog has been asked for.
 */
export function printHtml(html: string, doc: Document = document): Promise<void> {
  lastFrame?.remove();
  const frame = doc.createElement('iframe');
  lastFrame = frame;
  frame.className = 'pn-print-frame';
  // Same origin so the page can call print(); modals so print() isn't ignored. No scripts.
  frame.setAttribute('sandbox', 'allow-same-origin allow-modals');
  frame.setAttribute('aria-hidden', 'true');
  frame.tabIndex = -1;
  Object.assign(frame.style, {
    position: 'fixed',
    right: '0',
    bottom: '0',
    width: '0',
    height: '0',
    border: '0',
    visibility: 'hidden',
  });
  return new Promise((resolve, reject) => {
    frame.addEventListener(
      'load',
      () => {
        const win = frame.contentWindow;
        if (!win) {
          frame.remove();
          reject(new Error('Puffinote couldn’t print the note.'));
          return;
        }
        win.addEventListener('afterprint', () => {
          if (lastFrame === frame) lastFrame = null;
          frame.remove();
        });
        win.focus();
        win.print();
        resolve();
      },
      { once: true },
    );
    frame.srcdoc = html;
    doc.body.append(frame);
  });
}
