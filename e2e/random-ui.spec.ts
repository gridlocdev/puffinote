// Random use of the whole app: seeded runs of commands from the ⌘K menu, typing and keys, checking after every
// step that nothing broke. `just e2e` runs a few seeds; `just soak` tries new ones (FUZZ_RUNS, FUZZ_SEED_BASE).
// A failure prints its seed: `FUZZ_SEED=<n> just e2e -g random` replays it, with every step. Keep a found bug
// as its own scenario.
import { readdirSync, readFileSync, statSync } from 'node:fs';
import type { Page } from '@playwright/test';
import { DATA, expect, openApp, runCommand, test, waitForApp, watchErrors } from './fixtures.ts';
import { createRandom } from './randomOps.ts';

const STEPS = 40;

/**
 * Commands that leave the page, download a file, show the system's file picker, or use the clipboard, which in a
 * browser can be the owner's real one. Matched against the title of each ⌘K entry.
 */
const SKIP = [
  /print/i,
  /export/i,
  /download/i,
  /import/i,
  /open notebook/i,
  /default app/i,
  /^(cut|copy|paste)\b/i,
];

/** Keys worth pressing on their own, besides the letters random typing presses. Add the app's single-key shortcuts. */
const KEYS = [
  'Escape',
  'Enter',
  'Tab',
  'Shift+Tab',
  'Space',
  'ArrowUp',
  'ArrowDown',
  'ArrowLeft',
  'ArrowRight',
  'Shift+ArrowDown',
  'Home',
  'End',
  'Backspace',
  'Delete',
  'ControlOrMeta+KeyZ',
  'ControlOrMeta+Shift+KeyZ',
  'ControlOrMeta+KeyA',
  // Puffinote's own: open, close and move between notes, save, tick a task, format.
  'ControlOrMeta+KeyN',
  'ControlOrMeta+KeyO',
  'ControlOrMeta+KeyW',
  'ControlOrMeta+KeyS',
  'ControlOrMeta+KeyB',
  'ControlOrMeta+KeyI',
  'ControlOrMeta+Enter',
  'ControlOrMeta+Slash',
  'Control+Tab',
];

const runs = Number(process.env.FUZZ_RUNS ?? 3);
const base = Number(process.env.FUZZ_SEED_BASE ?? 1);
const seeds = process.env.FUZZ_SEED
  ? [Number(process.env.FUZZ_SEED)]
  : Array.from({ length: runs }, (_, i) => base + i);

/**
 * Open ⌘K and click one of its entries. Clicking runs exactly that entry (typing its name may match another one
 * first), and disabled entries are skipped (a click on one waits forever). Options are looked up inside the menu,
 * because list rows can be options too. Returns what was run, or why nothing was.
 */
async function runRandomCommand(page: Page, pick: (count: number) => number): Promise<string> {
  await page.keyboard.press('ControlOrMeta+KeyK');
  const menu = page.getByRole('dialog', { name: 'Command menu' });
  const opened = await menu
    .getByRole('combobox')
    .waitFor({ timeout: 1000 })
    .then(
      () => true,
      () => false,
    );
  // ⌘K opens over no dialog but its own kind, so with one open this is expected: the per-step check makes sure
  // that dialog kept the keyboard.
  if (!opened) return 'command menu did not open';
  const options = menu.getByRole('option');
  const entries = await options.evaluateAll((items) =>
    items.map((item) => ({
      // The title, without its shortcut or the ✓ of a setting that is on.
      label: (item.querySelector('.pn-option-title') ?? item).textContent?.replace('✓', '').trim() ?? '',
      enabled: item.getAttribute('aria-disabled') !== 'true',
    })),
  );
  const usable = entries
    .map((entry, i) => ({ ...entry, i }))
    .filter(({ label, enabled }) => enabled && !SKIP.some((pattern) => pattern.test(label)));
  const chosen = usable[pick(usable.length)];
  if (!chosen) {
    await page.keyboard.press('Escape');
    return 'no command to run';
  }
  // A dialog a command opens a moment later (a Delete… confirmation) replaces the menu before the click.
  const clicked = await options
    .nth(chosen.i)
    .click({ timeout: 1000 })
    .then(
      () => true,
      () => false,
    );
  if (!clicked && (await menu.count()) === 0) return `command menu was replaced before ${chosen.label}`;
  if (!clicked) throw new Error(`could not click ${chosen.label} in the command menu`);
  return `command ${chosen.label}`;
}

/**
 * Longer than anything the app waits for before an edit reaches the file: its autosave delay (Puffinote's is 1 s)
 * and any animation that runs before the change is made (Taskite's Space waits 350 ms before it completes a task).
 */
const SETTLE_MS = 2000;

/** The pause between random steps, on the page's clock. */
const STEP_PAUSE_MS = 50;

/**
 * `text` as a person could type it where the keyboard is. Playwright types a character that has no key on its
 * keyboard (é, 漢字, and a tab: Tab is a key, but `\t` isn't one of its characters) by inserting it as text,
 * which lands in the last text field, and takes the keyboard there, even when a button has it. No keyboard does
 * that, so outside a text field only the characters with keys are typed.
 */
async function typeable(page: Page, text: string): Promise<string> {
  const inText = await page.evaluate(() => {
    const active = document.activeElement;
    // A radio button or checkbox is an input too, but holds no text.
    const textInput = ['text', 'search', 'email', 'url', 'tel', 'password', 'number'];
    if (active instanceof HTMLInputElement) return textInput.includes(active.type);
    return (
      active instanceof HTMLTextAreaElement || (active instanceof HTMLElement && active.isContentEditable)
    );
  });
  return inText ? text : text.replace(/[^\x20-\x7e\n\r]/g, '');
}

/** Where the keyboard is, when a dialog is open and the keyboard is not in it; null otherwise. */
async function focusOutsideDialog(page: Page): Promise<string | null> {
  return page.evaluate(() => {
    const dialogs = [...document.querySelectorAll('[role=dialog], [role=alertdialog]')];
    const active = document.activeElement;
    if (dialogs.length === 0 || dialogs.some((dialog) => dialog.contains(active))) return null;
    const label = active?.getAttribute('aria-label') ?? '';
    return `${active?.tagName.toLowerCase()} ${label} while ${dialogs.map((d) => d.getAttribute('aria-label')).join(', ')} is open`;
  });
}

/** Every file in the notebook with its text, except Puffinote's own state in .puffinote. */
function notebookFiles(dir = DATA, prefix = ''): Record<string, string> {
  const files: Record<string, string> = {};
  for (const name of readdirSync(dir)) {
    if (prefix === '' && name === '.puffinote') continue;
    const file = `${dir}/${name}`;
    if (statSync(file).isDirectory()) Object.assign(files, notebookFiles(file, `${prefix}${name}/`));
    else files[`${prefix}${name}`] = readFileSync(file, 'latin1');
  }
  return files;
}

/** After a run: every open note is saved, and a reload shows the notebook without writing anything. */
async function checkSettled(page: Page): Promise<void> {
  // A dialog a command opens a moment later (a Delete… confirmation) can come up after the first Escape.
  const dialogs = page.locator('[role=dialog], [role=alertdialog]');
  for (let i = 0; i < 6; i++) {
    await page.clock.runFor(STEP_PAUSE_MS);
    if ((await dialogs.count()) === 0) break;
    await page.keyboard.press('Escape');
  }
  await expect(dialogs, 'Escape closes every dialog').toHaveCount(0);
  // Until these timers fire, a tab can look saved while an edit is still on its way to the file.
  await page.clock.runFor(SETTLE_MS);
  // Random use may have turned Auto-save off, so save the way a user would.
  await runCommand(page, 'Save All');
  await page.clock.runFor(SETTLE_MS);
  await expect(page.getByRole('button', { name: /\(unsaved\)$/ }), 'every open note is saved').toHaveCount(0);
  const files = notebookFiles();
  await openApp(page);
  await page.clock.runFor(SETTLE_MS);
  expect(notebookFiles(), 'a reload writes nothing').toEqual(files);
}

for (const runSeed of seeds) {
  test(`random use, seed ${runSeed}`, async ({ page, seed }) => {
    seed('basic');
    // A step that hangs fails here, with the steps that led to it, instead of at the test's own timeout,
    // which would end the test before the seed and the steps are printed.
    page.setDefaultTimeout(5000);
    const errors = watchErrors(page);
    await openApp(page);

    const random = createRandom(runSeed);
    const log: string[] = [];
    try {
      for (let step = 0; step < STEPS; step++) {
        const roll = random.int(10);
        if (roll < 4) {
          log.push(await runRandomCommand(page, (count) => random.int(count)));
        } else if (roll < 7) {
          const text = await typeable(page, random.text(8));
          log.push(`type ${JSON.stringify(text)}`);
          await page.keyboard.type(text);
        } else {
          const key = random.pick(KEYS);
          log.push(`key ${key}`);
          await page.keyboard.press(key);
        }
        // A person takes longer than this between keys. Without it, focus the app moves a moment after a command
        // (to the editor, say) lands after the next key has opened a dialog, which no one could do.
        await page.clock.runFor(STEP_PAUSE_MS);
        // The notebook switcher's last entry leaves for the start screen; open the fixture notebook again.
        const reopen = page.getByRole('button', { name: 'Open “E2E”' });
        if (await reopen.isVisible()) {
          log.push('back to the notebook');
          await reopen.click();
          await waitForApp(page);
        }
        expect(errors, 'console errors').toEqual([]);
        expect(await focusOutsideDialog(page), 'an open dialog has the keyboard').toBeNull();
      }
      log.push('settle');
      await checkSettled(page);
      expect(errors, 'console errors').toEqual([]);
    } catch (error) {
      const message = error instanceof Error ? error.message : String(error);
      const steps = process.env.FUZZ_SEED ? log : log.slice(-20);
      throw new Error(`FUZZ_SEED=${runSeed} failed after:\n${steps.join('\n')}\n\n${message}`, {
        cause: error,
      });
    }
  });
}
