import {
  expect,
  openApp,
  openNote,
  readDataIfThere,
  runCommand,
  test,
  watchErrors,
  writeData,
} from './fixtures.ts';

test.describe('Desktop, browser and NAS', () => {
  test('on a slow NAS, a dialog opened while a new note is still being written keeps the keyboard', async ({
    seed,
    page,
  }) => {
    seed('basic');
    await openApp(page);
    // Hold the new note's write until Quick Open is up.
    let release = () => {};
    const held = new Promise<void>((resolve) => (release = resolve));
    await page.route(/\/api\/file\?.*path=Plan\.md/, async (route) => {
      if (route.request().method() === 'PUT') await held;
      await route.continue();
    });
    await page.keyboard.press('ControlOrMeta+KeyN');
    await page.keyboard.type('Plan');
    await page.keyboard.press('Enter');
    await page.keyboard.press('ControlOrMeta+KeyO');
    const box = page.getByRole('dialog', { name: 'Quick Open' }).getByRole('combobox');
    await expect(box).toBeFocused();
    release();
    await expect(page.getByRole('tab', { name: /Plan/ })).toBeVisible();
    await page.clock.runFor(100);
    await expect(box).toBeFocused();
  });

  test('on a slow NAS, renaming a folder while a new note in it is still being written keeps the note', async ({
    seed,
    page,
  }) => {
    seed('basic');
    await openApp(page);
    const errors = watchErrors(page);
    let release = () => {};
    const held = new Promise<void>((resolve) => (release = resolve));
    await page.route(/\/api\/file\?.*path=Trips%2FPlan\.md/, async (route) => {
      if (route.request().method() === 'PUT') await held;
      await route.continue();
    });
    const trips = page.getByRole('treeitem', { name: 'Trips' });
    await trips.click({ button: 'right' });
    await page.getByRole('menuitem', { name: 'New Note…' }).click();
    await page.keyboard.type('Plan');
    await page.keyboard.press('Enter');
    await trips.click({ button: 'right' });
    await page.getByRole('menuitem', { name: 'Rename…' }).click();
    await page.keyboard.type('Travel');
    await page.keyboard.press('Enter');
    release();
    await expect(page.getByRole('tab', { name: /Plan/ })).toBeVisible();
    await expect.poll(() => readDataIfThere('Travel/Plan.md')).toBe('');
    await expect.poll(() => readDataIfThere('Trips/Plan.md')).toBeUndefined();
    expect(errors).toEqual([]);
  });

  test('on a slow NAS, a second rename started before the first lands renames the note where it went', async ({
    seed,
    page,
  }) => {
    seed('basic');
    await openApp(page);
    const errors = watchErrors(page);
    await openNote(page, 'welcome');
    const text = readDataIfThere('Welcome.md');
    let release = () => {};
    const held = new Promise<void>((resolve) => (release = resolve));
    await page.route(/\/api\/move\?/, async (route) => {
      await held;
      await route.continue();
    });
    await runCommand(page, 'Rename…');
    await page.keyboard.type('Hello');
    await page.keyboard.press('Enter');
    await runCommand(page, 'Rename…');
    await page.keyboard.type('Greetings');
    await page.keyboard.press('Enter');
    release();
    await expect.poll(() => readDataIfThere('Greetings.md')).toBe(text);
    expect(readDataIfThere('Welcome.md')).toBeUndefined();
    expect(readDataIfThere('Hello.md')).toBeUndefined();
    await expect(page.getByRole('tab', { name: /Greetings/ })).toBeVisible();
    expect(errors).toEqual([]);
  });

  test('on a slow NAS, renaming a folder while a refresh reads a note in it moves the note it read', async ({
    seed,
    page,
  }) => {
    seed('basic');
    await openApp(page);
    const errors = watchErrors(page);
    await openNote(page, 'packing');
    writeData('Trips/Packing list.md', '# Packing list\n\n- Sun hat\n');
    let release = () => {};
    const held = new Promise<void>((resolve) => (release = resolve));
    await page.route(/\/api\/file\?.*path=Trips%2FPacking/, async (route) => {
      if (route.request().method() === 'GET') await held;
      await route.continue();
    });
    await runCommand(page, 'Refresh Notebook');
    const trips = page.getByRole('treeitem', { name: 'Trips' });
    await trips.click({ button: 'right' });
    await page.getByRole('menuitem', { name: 'Rename…' }).click();
    await page.keyboard.type('Travel');
    await page.keyboard.press('Enter');
    release();
    await expect(page.getByRole('main').getByText('Sun hat')).toBeVisible();
    await expect.poll(() => readDataIfThere('Travel/Packing list.md')).toContain('Sun hat');
    expect(errors).toEqual([]);
  });
});
