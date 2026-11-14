import { describe, expect, it } from 'vitest';
import { NativeStore, type PuffinoteNative } from '../src/platform/nativeStore.ts';

/** A bridge that records the paths it is asked about and lets the test send change events. */
function fakeBridge(platform: string) {
  const calls: string[] = [];
  let changed: (paths: readonly string[]) => void = () => undefined;
  const bridge = {
    platform,
    list: async (dir: string) => (calls.push(dir), []),
    read: async (path: string) => (
      calls.push(path),
      { bytes: new Uint8Array(), modified: 1, version: '1:0:1' }
    ),
    watch: (root: string | null) => void (root && calls.push(root)),
    onChanged: (cb: (paths: readonly string[]) => void) => ((changed = cb), () => undefined),
  } as unknown as PuffinoteNative;
  return { bridge, calls, send: (paths: string[]) => changed(paths) };
}

describe('NativeStore on Windows', () => {
  it('reports changes under a root given with backslashes', () => {
    const { bridge, send } = fakeBridge('win32');
    const store = new NativeStore(bridge, 'C:\\Users\\me\\Notes\\');
    const seen: string[][] = [];
    store.watch((paths) => seen.push([...paths]));
    send(['C:\\Users\\me\\Notes\\a.md', 'C:\\Users\\me\\Notes\\sub\\b.md', 'C:\\Users\\me\\Other\\c.md']);
    expect(seen).toEqual([['a.md', 'sub/b.md']]);
    expect(store.name).toBe('Notes');
  });

  it('matches the root whatever the case of the drive letter', () => {
    const { bridge, send } = fakeBridge('win32');
    const store = new NativeStore(bridge, 'C:\\Notes');
    const seen: string[][] = [];
    store.watch((paths) => seen.push([...paths]));
    send(['c:\\Notes\\a.md']);
    expect(seen).toEqual([['a.md']]);
  });

  it('hands the bridge Windows paths with backslashes only', async () => {
    const { bridge, calls } = fakeBridge('win32');
    const store = new NativeStore(bridge, 'C:\\Notes');
    await store.read('sub/a.md');
    expect(calls).toEqual(['C:\\Notes\\sub\\a.md']);
  });

  it('keeps case when matching on Linux', () => {
    const { bridge, send } = fakeBridge('linux');
    const store = new NativeStore(bridge, '/home/me/Notes');
    const seen: string[][] = [];
    store.watch((paths) => seen.push([...paths]));
    send(['/home/me/notes/a.md', '/home/me/Notes/b.md']);
    expect(seen).toEqual([['b.md']]);
  });
});

describe('NativeStore versions', () => {
  it('passes the version from a read back to the next write whole', async () => {
    const sent: (string | null)[] = [];
    const bridge = {
      platform: 'darwin',
      read: async () => ({ bytes: new Uint8Array(), modified: 1000, version: '1000:5:42' }),
      write: async (_p: string, _b: Uint8Array, expected: string | null) => (
        sent.push(expected),
        '2000:6:43'
      ),
    } as unknown as PuffinoteNative;
    const store = new NativeStore(bridge, '/n');
    const { version } = await store.read('a.md');
    expect(version).toBe('1000:5:42');
    expect(await store.write('a.md', new Uint8Array(), { expected: version })).toBe('2000:6:43');
    expect(sent).toEqual(['1000:5:42']);
  });
});
