import { BrowserWindow, session } from 'electron';
import { mkdtemp, rm, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

/** The session print windows use: in memory, and loading nothing but the page itself and `data:` pictures. */
const PARTITION = 'puffinote-pdf';
const allowed = new Set<string>();
let configured = false;

function pdfSession(): Electron.Session {
  const s = session.fromPartition(PARTITION);
  if (!configured) {
    configured = true;
    s.webRequest.onBeforeRequest((details, callback) => {
      callback({ cancel: !(details.url.startsWith('data:') || allowed.has(details.url)) });
    });
    s.setPermissionRequestHandler((_wc, _permission, callback) => callback(false));
  }
  return s;
}

/**
 * Lay a standalone HTML document (a note's export, PRD §10.5) out for print in a hidden window with scripts
 * off, and return the PDF. Its print stylesheet sets the page margins. Nothing outside the document loads.
 */
export async function htmlToPdf(html: string): Promise<Uint8Array> {
  const dir = await mkdtemp(path.join(os.tmpdir(), 'puffinote-pdf-'));
  const file = path.join(dir, 'note.html');
  const url = pathToFileURL(file).href;
  const win = new BrowserWindow({
    show: false,
    webPreferences: {
      session: pdfSession(),
      sandbox: true,
      contextIsolation: true,
      nodeIntegration: false,
      javascript: false,
    },
  });
  win.webContents.setWindowOpenHandler(() => ({ action: 'deny' }));
  win.webContents.on('will-navigate', (e) => e.preventDefault());
  allowed.add(url);
  try {
    await writeFile(file, html);
    await win.loadURL(url);
    const pdf = await win.webContents.printToPDF({
      printBackground: true,
      preferCSSPageSize: true,
      generateDocumentOutline: true,
    });
    return new Uint8Array(pdf.buffer, pdf.byteOffset, pdf.byteLength);
  } finally {
    allowed.delete(url);
    win.destroy();
    await rm(dir, { recursive: true, force: true });
  }
}
