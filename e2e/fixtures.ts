// Shared scenario setup. `seed('basic')` replaces .e2e-data with a copy of e2e/fixtures/basic, so each test
// starts from known notes and nothing one test writes leaks into the next. Every page runs on a clock that starts
// at NOW and keeps going; `page.clock.runFor(ms)` moves it on, which fires the autosave without waiting for it.
import { expect, test as base, type Page } from '@playwright/test';
import { NOW, seedData } from './server.ts';

export { expect };
export { DATA, NOW, readData, readDataIfThere, seedData, writeData } from './server.ts';

export const test = base.extend<{ seed: (fixture: string) => void }>({
  page: async ({ page }, use) => {
    await page.clock.install({ time: NOW });
    await use(page);
  },
  // eslint-disable-next-line no-empty-pattern -- Playwright reads fixture names from this pattern.
  seed: async ({}, use) => {
    await use(seedData);
  },
});

/**
 * Open the app the way a user does and wait until it has drawn. The page loads before the app has opened the
 * notebook, and a key pressed sooner goes nowhere.
 */
export async function openApp(page: Page): Promise<void> {
  await page.goto('/');
  await waitForApp(page);
}

/** The shell has drawn the notebook: "Opening …" has its own `<main>`, but no status line. */
export async function waitForApp(page: Page): Promise<void> {
  await page.getByRole('main').getByRole('status').waitFor({ state: 'attached' });
}

/**
 * Failed requests the app expects and handles: its own files it reads before they exist, and a create-only
 * write of a .gitignore that is already there.
 */
const EXPECTED: readonly { status: number; method: string; url: RegExp }[] = [
  { status: 404, method: 'GET', url: /\/api\/file\?.*path=\.puffinote%2F(settings|session)\.json$/ },
  { status: 404, method: 'GET', url: /\/api\/list\?.*path=\.puffinote%2Ftrash$/ },
  { status: 412, method: 'PUT', url: /\/api\/file\?.*path=\.puffinote%2F(trash%2F)?\.gitignore$/ },
];

/**
 * Collect what went wrong on the page: uncaught errors, console errors and failed requests, by URL. The
 * browser's own "Failed to load resource" line names no URL, so the request is reported instead.
 */
export function watchErrors(page: Page): string[] {
  const errors: string[] = [];
  page.on('pageerror', (error) => errors.push(error.message));
  page.on('console', (message) => {
    if (message.type() === 'error' && !message.text().startsWith('Failed to load resource'))
      errors.push(message.text());
  });
  page.on('response', (response) => {
    const status = response.status();
    const method = response.request().method();
    const url = response.url();
    if (status >= 400 && !EXPECTED.some((e) => e.status === status && e.method === method && e.url.test(url)))
      errors.push(`${status} ${method} ${url}`);
  });
  return errors;
}

/** The ⌘K menu. Its options are looked up inside it, because list rows can be options too. */
export const commandMenu = (page: Page) => page.getByRole('dialog', { name: 'Command menu' });

/**
 * Run a command from ⌘K by its name. Type the way a person does: wait until focus is in the field, then use the
 * keyboard. `fill()` right after a field appears can land before the component has set itself up, and be lost.
 */
export async function runCommand(page: Page, name: string): Promise<void> {
  await page.keyboard.press('ControlOrMeta+KeyK');
  const menu = commandMenu(page);
  await expect(menu.getByRole('combobox')).toBeFocused();
  await page.keyboard.type(name);
  await page.keyboard.press('Enter');
  await expect(menu).toBeHidden();
}

/** Open a note with ⌘O by part of its name, as a user would; the caret lands in it. */
export async function openNote(page: Page, name: string): Promise<void> {
  await page.keyboard.press('ControlOrMeta+KeyO');
  const dialog = page.getByRole('dialog', { name: 'Quick Open' });
  await expect(dialog.getByRole('combobox')).toBeFocused();
  await page.keyboard.type(name);
  await page.keyboard.press('Enter');
  await expect(dialog).toBeHidden();
  await expect(editor(page)).toBeFocused();
}

/** The editor's input. It is a hidden field at the caret, so check focus, not visibility. */
export const editor = (page: Page) => page.getByRole('textbox', { name: 'Markdown editor' });

/** Autosave waits a second after the last edit (PRD §5); run the page clock past it. */
export async function letAutosave(page: Page): Promise<void> {
  await page.clock.runFor(1500);
}

/** The Settings dialog, with `tab` selected. */
export async function openSettings(page: Page, tab: 'Preferences' | 'This Notebook' = 'Preferences') {
  await runCommand(page, 'Settings…');
  const dialog = page.getByRole('dialog', { name: 'Settings' });
  await dialog.getByRole('tab', { name: tab }).click();
  return dialog;
}
