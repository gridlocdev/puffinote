import { describe, expect, it, vi } from 'vitest';
import { MAX_EMBED_BYTES, noteHtml, toBase64 } from '../src/notebook/export.ts';
import { Notebook } from '../src/notebook/notebook.ts';
import { memoryPrefs } from '../src/notebook/settings.ts';
import { MemoryStore } from '../src/platform/memoryStore.ts';
import { printHtml } from '../src/platform/print.ts';

const png = new Uint8Array([0x89, 0x50, 0x4e, 0x47, 1, 2, 3]);

describe('Export as HTML', () => {
  it('embeds the pictures a note shows as data: URLs, read relative to the note', async () => {
    const store = new MemoryStore('N', { 'a/pics/cat.png': png, 'top.jpg': png });
    const out = await noteHtml(store, 'a/Trip.md', '# Trip\n\n![cat](pics/cat.png) ![t](../top.jpg)');
    expect(out.name).toBe('Trip.html');
    expect(out.html).toContain('<title>Trip</title>');
    expect(out.html).toContain(`<img src="data:image/png;base64,${toBase64(png)}" alt="cat">`);
    expect(out.html).toContain('<img src="data:image/jpeg;base64,');
    expect(out.html).toContain('prefers-color-scheme: dark');
    expect(out.skipped).toEqual([]);
  });

  it('decodes an encoded path and reads each picture once', async () => {
    const store = new MemoryStore('N', { 'my cat.png': png });
    const read = vi.spyOn(store, 'read');
    const out = await noteHtml(store, 'n.md', '![a](my%20cat.png)\n![b](my%20cat.png)');
    expect(out.html.match(/data:image\/png/g)).toHaveLength(2);
    expect(read).toHaveBeenCalledTimes(1);
  });

  it('leaves web pictures alone and keeps missing, SVG and oversized ones as links', async () => {
    const big = new Uint8Array(MAX_EMBED_BYTES + 1);
    const store = new MemoryStore('N', { 'big.png': big, 'v.svg': '<svg/>' });
    const md = '![w](https://x.com/a.png) ![m](gone.png) ![s](v.svg) ![b](big.png)';
    const out = await noteHtml(store, 'n.md', md);
    expect(out.html).toContain('<img src="https://x.com/a.png" alt="w">');
    expect(out.html).toContain('<img src="gone.png" alt="m">');
    expect(out.html).toContain('<img src="v.svg" alt="s">');
    expect(out.html).toContain('<img src="big.png" alt="b">');
    expect(out.html).not.toContain('data:');
    expect(out.skipped).toEqual(['gone.png', 'v.svg', 'big.png']);
  });

  it('exports the note in front, unsaved text included, as a download', async () => {
    const store = new MemoryStore('N', { 'n.md': 'saved', 'p.png': png });
    const download = vi.fn();
    const nb = await Notebook.open(store, { prefs: memoryPrefs({ autoSave: false }), download });
    const tab = (await nb.openFile('n.md'))!;
    tab.state!.select(5);
    tab.state!.insert(' **unsaved** ![p](p.png) ![x](x.png)');
    await nb.transfer.exportHtml(tab);
    expect(download).toHaveBeenCalledTimes(1);
    const [name, bytes, mime] = download.mock.calls[0]!;
    expect(name).toBe('n.html');
    expect(mime).toBe('text/html');
    const html = new TextDecoder().decode(bytes as Uint8Array);
    expect(html).toContain('<strong>unsaved</strong>');
    expect(html).toContain('data:image/png;base64,');
    expect(nb.notice.value?.text).toContain('“x.png”');
  });
});

describe('Wiki embeds in export and copy', () => {
  it('exports a wiki embed as the picture the notebook finds by name, with its width', async () => {
    const store = new MemoryStore('N', { 'a/n.md': 'x', 'attachments/cat.png': png, 'b/dog.png': png });
    const nb = await Notebook.open(store, { prefs: memoryPrefs({ autoSave: false }) });
    const out = await noteHtml(
      store,
      'a/n.md',
      '![[cat.png|300]]\n\n![[gone.png]]\n\n![[Other note]]',
      (n, r) => nb.pictures.pathOf(n, r),
    );
    expect(out.html).toContain(
      `<img src="data:image/png;base64,${toBase64(png)}" alt="cat.png" width="300">`,
    );
    expect(out.html).toContain('<span class="embed">gone.png</span>');
    expect(out.html).toContain('<span class="embed">Other note</span>');
    expect(out.skipped).toEqual(['gone.png']);
  });
});

describe('Copy with pictures', () => {
  it('gives a fragment of formatted text with linked and embedded pictures inside it', async () => {
    const store = new MemoryStore('N', { 'a/n.md': 'x', 'a/p.png': png, 'attachments/cat.png': png });
    const nb = await Notebook.open(store, { prefs: memoryPrefs({ autoSave: false }) });
    const html = await nb.transfer.copyHtml('a/n.md', 'some **bold**\n\n![p](p.png)\n\n![[cat.png]]');
    expect(html).not.toContain('<html');
    expect(html).toContain('<strong>bold</strong>');
    expect(html).toContain(`<img src="data:image/png;base64,${toBase64(png)}" alt="p">`);
    expect(html).toContain(`<img src="data:image/png;base64,${toBase64(png)}" alt="cat.png">`);
    expect(nb.notice.value).toBeNull();
  });

  it('says which pictures stayed links', async () => {
    const store = new MemoryStore('N', { 'n.md': 'x' });
    const nb = await Notebook.open(store, { prefs: memoryPrefs({ autoSave: false }) });
    const html = await nb.transfer.copyHtml('n.md', '![m](gone.png)');
    expect(html).toContain('<img src="gone.png" alt="m">');
    expect(nb.notice.value?.text).toContain('Copied without “gone.png”');
  });
});

describe('Export as PDF', () => {
  it('hands the same standalone HTML to the host to print, named after the note', async () => {
    const store = new MemoryStore('N', { 'a/n.md': '# Hi', 'a/p.png': png });
    const print = vi.fn(async () => {});
    const nb = await Notebook.open(store, { prefs: memoryPrefs({ autoSave: false }), print });
    expect(nb.transfer.canPrint).toBe(true);
    const tab = (await nb.openFile('a/n.md'))!;
    tab.state!.select(4);
    tab.state!.insert('\n\n![p](p.png)');
    await nb.transfer.exportPdf(tab);
    expect(print).toHaveBeenCalledTimes(1);
    const [html, name] = print.mock.calls[0]! as unknown as [string, string];
    expect(name).toBe('n.pdf');
    expect(html).toContain('<h1>Hi</h1>');
    expect(html).toContain('data:image/png;base64,');
    expect(html).toContain('@media print');
  });

  it('is off where the host can’t print', async () => {
    const nb = await Notebook.open(new MemoryStore('N', { 'n.md': '' }), { prefs: memoryPrefs() });
    expect(nb.transfer.canPrint).toBe(false);
  });

  it('prints from a hidden, sandboxed frame and removes it after the dialog', async () => {
    const listeners = new Map<string, () => void>();
    const winListeners = new Map<string, () => void>();
    const win = {
      print: vi.fn(),
      focus: vi.fn(),
      addEventListener: (type: string, cb: () => void) => winListeners.set(type, cb),
    };
    const attrs = new Map<string, string>();
    const frame = {
      style: {},
      tabIndex: 0,
      srcdoc: '',
      contentWindow: win,
      className: '',
      remove: vi.fn(),
      setAttribute: (k: string, v: string) => attrs.set(k, v),
      addEventListener: (type: string, cb: () => void) => listeners.set(type, cb),
    };
    const doc = { createElement: () => frame, body: { append: vi.fn() } };
    const done = printHtml('<p>x</p>', doc as unknown as Document);
    expect(frame.srcdoc).toBe('<p>x</p>');
    expect(attrs.get('sandbox')).toBe('allow-same-origin allow-modals');
    expect(doc.body.append).toHaveBeenCalledWith(frame);
    listeners.get('load')!();
    await done;
    expect(win.print).toHaveBeenCalledTimes(1);
    expect(frame.remove).not.toHaveBeenCalled();
    winListeners.get('afterprint')!();
    expect(frame.remove).toHaveBeenCalled();
  });
});
