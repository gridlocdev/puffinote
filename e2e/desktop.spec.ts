// Desktop smoke test: the built Electron app starts headless on a throwaway profile, opens a copy of the
// fixture notebook named on the command line, and shows it. `just e2e-desktop` builds it first. The throwaway
// profile matters: the app reopens the notebooks its profile remembers, and it must never open the owner's.
import { cpSync, mkdirSync, mkdtempSync, realpathSync, rmSync, writeFileSync } from 'node:fs';
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
    // Opened before: the state folder's .gitignore is there, so making it again conflicts, as it does on every
    // launch after the first.
    mkdirSync(path.join(notebook, '.puffinote'));
    writeFileSync(path.join(notebook, '.puffinote', '.gitignore'), 'session.json\n');
    const app = await electron.launch({
      cwd: DESKTOP,
      // The main script by name: '.' would also count as a folder to open.
      args: ['out/main.js', `--user-data-dir=${profile}`, notebook],
      env: { ...process.env, PUFFINOTE_HEADLESS: '1', PUFFINOTE_NO_UPDATE_CHECK: '1' },
    });
    let mainLog = '';
    app.process().stderr?.on('data', (chunk: Buffer) => (mainLog += chunk.toString()));
    try {
      expect(await app.evaluate(({ app }) => app.getPath('userData'))).toBe(profile);
      const window = await app.firstWindow();
      const errors = watchErrors(window);
      await waitForApp(window);
      await expect(
        window.getByRole('tree', { name: 'Files' }).getByRole('treeitem', { name: 'Welcome.md' }),
      ).toBeVisible();
      expect(
        await app.evaluate(({ BrowserWindow }) => BrowserWindow.getAllWindows().some((w) => w.isVisible())),
        'no window shows on the screen',
      ).toBe(false);
      expect(errors).toEqual([]);
      // Conflicts and missing files are normal results, not handler errors Electron prints to the terminal.
      expect(mainLog, 'the main process prints no handler errors').not.toContain('Error occurred in handler');
    } finally {
      await app.close();
      rmSync(temp, { recursive: true, force: true });
    }
  });
});
