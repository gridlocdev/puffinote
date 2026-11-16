import { expect, openApp, openNote, openSettings, test } from './fixtures.ts';

test.describe('Two layouts', () => {
  test.beforeEach(async ({ seed, page }) => {
    seed('basic');
    await openApp(page);
  });

  test('the icon rail layout looks right in light and dark', async ({ page }) => {
    await openNote(page, 'welcome');
    await expect(page).toHaveScreenshot('rail-light.png');
    await page.emulateMedia({ colorScheme: 'dark' });
    await expect(page).toHaveScreenshot('rail-dark.png');
  });

  test('the Library layout shows folders, a list of notes and one note', async ({ page }) => {
    const settings = await openSettings(page);
    await settings.getByRole('combobox', { name: 'Layout' }).selectOption({ label: 'Library and note list' });
    await settings.getByRole('button', { name: 'Done' }).click();
    await expect(page.getByRole('navigation', { name: 'Library' })).toBeVisible();
    await openNote(page, 'welcome');
    await expect(page).toHaveScreenshot('library-light.png');
    await page.emulateMedia({ colorScheme: 'dark' });
    await expect(page).toHaveScreenshot('library-dark.png');
  });
});
