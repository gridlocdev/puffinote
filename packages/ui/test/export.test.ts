import { describe, expect, it, vi } from 'vitest';
import { MAX_EMBED_BYTES, noteHtml, toBase64 } from '../src/notebook/export.ts';
import { Notebook } from '../src/notebook/notebook.ts';
import { memoryPrefs } from '../src/notebook/settings.ts';
import { MemoryStore } from '../src/platform/memoryStore.ts';

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
    await nb.exportHtml(tab);
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
