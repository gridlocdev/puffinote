import { describe, expect, it } from 'vitest';
import { NotebookSettings, overridesPath } from '../src/notebook/notebookSettings.ts';
import { memoryPrefs } from '../src/notebook/settings.ts';
import { MemoryStore } from '../src/platform/memoryStore.ts';

function setup(files: Record<string, string> = {}, prefs = memoryPrefs()) {
  const store = new MemoryStore('N', files);
  const errors: unknown[] = [];
  const config = new NotebookSettings({ store, prefs, closed: () => false, report: (e) => errors.push(e) });
  return { store, config, errors };
}

const onDisk = async (store: MemoryStore) =>
  JSON.parse(new TextDecoder().decode((await store.read(overridesPath)).bytes)) as Record<string, unknown>;

describe('NotebookSettings', () => {
  it('resolves the notebook’s overrides over the user’s preferences', async () => {
    const { config } = setup(
      { [overridesPath]: JSON.stringify({ layout: 'library' }) },
      memoryPrefs({ layout: 'tabs', autoSave: false }),
    );
    await config.load();
    expect(config.settings.value.layout).toBe('library');
    expect(config.settings.value.autoSave).toBe(false);
  });

  it('reads a settings file that isn’t JSON as no overrides', async () => {
    const { config } = setup({ [overridesPath]: '{ nope' });
    await config.load();
    expect(config.overrides.value).toEqual({});
  });

  it('writes only what changed, keeping what this version doesn’t know', async () => {
    const { store, config, errors } = setup({
      [overridesPath]: JSON.stringify({ future: 1, autoSave: false }),
    });
    await config.load();
    await config.setOverrides({ autoSave: false, layout: 'library' });
    expect(errors).toEqual([]);
    expect(await onDisk(store)).toEqual({ future: 1, autoSave: false, layout: 'library' });
    expect(config.settings.value.layout).toBe('library');
  });

  it('takes another program’s change on reload', async () => {
    const { store, config } = setup();
    await config.load();
    await store.write(overridesPath, new TextEncoder().encode(JSON.stringify({ autoSave: false })), {
      create: true,
    });
    await config.reload();
    expect(config.overrides.value).toEqual({ autoSave: false });
  });

  it('saves the user’s preferences, over another window’s changes to the others', () => {
    const prefs = memoryPrefs({ autoSave: true });
    const { config } = setup({}, prefs);
    const base = config.userPrefs.value;
    prefs.save({ autoSave: true, layout: 'library' }); // another window
    config.setPreferences({ ...base, autoSave: false }, base);
    expect(prefs.load()).toEqual({ autoSave: false, layout: 'library' });
    expect(config.userPrefs.value).toEqual({ autoSave: false, layout: 'library' });
  });
});
