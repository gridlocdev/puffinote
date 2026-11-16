// Desktop smoke test: the built Electron app starts headless on a throwaway profile, opens a copy of the
// fixture notebook named on the command line, and shows it. `just e2e-desktop` builds it first. The throwaway
// profile matters: the app reopens the notebooks its profile remembers, and it must never open the owner's.
import { cpSync, mkdtempSync, realpathSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { _electron as electron } from '@playwright/test';
import { expect, test, waitForApp, watchErrors } from './fixtures.ts';

const DESKTOP = fileURLToPath(new URL('../apps/desktop', import.meta.url));
const FIXTURE = fileURLToPath(new URL('./fixtures/basic', import.meta.url));

test.describe('Desktop, browser and NAS', () => {
  test('the desktop app opens a notebook named on the command line, on a throwaway profile', async () => {
    const temp = realpathSync(mkdtempSync(path.join(tmpdir(), 'puffinote-e2e-')));
    const profile = path.join(temp, 'profile');
    const notebook = path.join(temp, 'Notebook');
    cpSync(FIXTURE, notebook, { recursive: true });
    const app = await electron.launch({
      cwd: DESKTOP,
      // The main script by name: '.' would also count as a folder to open.
      args: ['out/main.js', `--user-data-dir=${profile}`, notebook],
      env: { ...process.env, PUFFINOTE_HEADLESS: '1', PUFFINOTE_NO_UPDATE_CHECK: '1' },
    });
    try {
      expect(await app.evaluate(({ app }) => app.getPath('userData'))).toBe(profile);
      const window = await app.firstWindow();
      const errors = watchErrors(window);
      await waitForApp(window);
      await expect(
        window.getByRole('tree', { name: 'Files' }).getByRole('treeitem', { name: 'Welcome.md' }),
      ).toBeVisible();
      expect(errors).toEqual([]);
    } finally {
      await app.close();
      rmSync(temp, { recursive: true, force: true });
    }
  });
});
