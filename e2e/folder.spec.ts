import {
  editor,
  expect,
  letAutosave,
  openApp,
  openNote,
  openSettings,
  readData,
  readDataIfThere,
  runCommand,
  test,
  writeData,
} from './fixtures.ts';

test.describe('Keeping up with the folder', () => {
  test.beforeEach(async ({ seed, page }) => {
    seed('basic');
    await openApp(page);
  });

  test('an edit is saved to disk a second after typing stops, and is there after a reload', async ({
    page,
  }) => {
    await openNote(page, 'empty');
    await page.keyboard.type('Ferns like shade.');
    await letAutosave(page);
    await expect.poll(() => readData('Empty.md')).toBe('Ferns like shade.');
    await openApp(page);
    await openNote(page, 'empty');
    await expect(page.getByRole('main').getByText('Ferns like shade.', { exact: true })).toBeVisible();
  });

  test('a note changed by another app shows the new text', async ({ page }) => {
    await openNote(page, 'café');
    writeData('Café ☕ notes.md', '# Café ☕ notes\n\nRewritten somewhere else.\n');
    await runCommand(page, 'Refresh Notebook');
    await expect(page.getByRole('main').getByText('Rewritten somewhere else.')).toBeVisible();
  });

  test('a note changed by another app under unsaved edits is flagged, and Keep My Version writes the edits', async ({
    page,
  }) => {
    const settings = await openSettings(page);
    await settings.getByRole('checkbox', { name: 'Save automatically' }).uncheck();
    await settings.getByRole('button', { name: 'Done' }).click();
    await openNote(page, 'café');
    await page.keyboard.type('Mine. ');
    writeData('Café ☕ notes.md', 'Theirs.\n');
    await runCommand(page, 'Refresh Notebook');
    await expect(page.getByText('“Café ☕ notes.md” changed on disk since you opened it.')).toBeVisible();
    // Nothing is written over their version until the user chooses, and the edit is still there.
    await letAutosave(page);
    expect(readData('Café ☕ notes.md')).toBe('Theirs.\n');
    await expect(page.getByRole('main').getByText(/Mine\./)).toBeVisible();
    await page.getByRole('button', { name: 'Keep My Version' }).click();
    await expect.poll(() => readData('Café ☕ notes.md')).toMatch(/^Mine\. # Café/);
    await expect(page.getByText('changed on disk since you opened it.')).toBeHidden();
    await expect(editor(page)).toBeAttached();
  });

  test('Reload from Disk takes the other app’s version over unsaved edits', async ({ page }) => {
    const settings = await openSettings(page);
    await settings.getByRole('checkbox', { name: 'Save automatically' }).uncheck();
    await settings.getByRole('button', { name: 'Done' }).click();
    await openNote(page, 'café');
    await page.keyboard.type('Mine. ');
    writeData('Café ☕ notes.md', 'Theirs.\n');
    await runCommand(page, 'Refresh Notebook');
    await page.getByRole('button', { name: 'Reload from Disk' }).click();
    await expect(page.getByRole('main').getByText('Theirs.')).toBeVisible();
    await expect(page.getByRole('main').getByText(/Mine\./)).toBeHidden();
    expect(readData('Café ☕ notes.md')).toBe('Theirs.\n');
  });
});

test.describe('Settings per notebook', () => {
  test('a setting for this notebook is saved inside the notebook', async ({ seed, page }) => {
    seed('basic');
    await openApp(page);
    const settings = await openSettings(page, 'This Notebook');
    await settings.getByRole('combobox', { name: 'Layout' }).selectOption({ label: 'Library and note list' });
    await settings.getByRole('button', { name: 'Done' }).click();
    await expect(page.getByRole('navigation', { name: 'Library' })).toBeVisible();
    await expect.poll(() => readDataIfThere('.puffinote/settings.json')).toMatch(/"layout":\s*"library"/);
  });
});
