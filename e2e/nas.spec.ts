import { expect, openApp, test } from './fixtures.ts';

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
});
