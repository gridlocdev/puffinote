// Every test sits in a test.describe named after its docs/features.md heading, so the feature map test finds it.
import { commandMenu, expect, openApp, openNote, runCommand, test, watchErrors } from './fixtures.ts';

test.describe('Finding things', () => {
  test.beforeEach(async ({ seed, page }) => {
    seed('basic');
    await openApp(page);
  });

  test('starts with no errors', async ({ page }) => {
    const errors = watchErrors(page);
    await openApp(page);
    await expect(
      page.getByRole('tree', { name: 'Files' }).getByRole('treeitem', { name: 'Welcome.md' }),
    ).toBeVisible();
    expect(errors).toEqual([]);
  });

  test('the command menu runs a command', async ({ page }) => {
    await openNote(page, 'welcome');
    await runCommand(page, 'View Raw Text');
    await expect(
      page.getByRole('group', { name: 'View' }).getByRole('button', { name: 'Raw' }),
    ).toHaveAttribute('aria-pressed', 'true');
  });

  test('the command menu shows each command’s shortcut', async ({ page }) => {
    await page.keyboard.press('ControlOrMeta+KeyK');
    await expect(commandMenu(page).getByRole('combobox')).toBeFocused();
    await page.keyboard.type('Go to File');
    await expect(commandMenu(page).getByRole('option').first()).toContainText(/⌘O|Ctrl\+O/);
  });

  test('⌘O opens a note in a folder by part of its name', async ({ page }) => {
    await openNote(page, 'packing');
    await expect(page.getByRole('tab', { name: /Packing list/, selected: true })).toBeVisible();
    await expect(page.getByText('Rain jacket')).toBeVisible();
  });

  test('Search finds a word inside a note', async ({ page }) => {
    await page.getByRole('navigation', { name: 'Panels' }).getByRole('button', { name: 'Search' }).click();
    await page.keyboard.type('plumber');
    await page.getByRole('complementary').getByText('Call the plumber').first().click();
    await expect(page.getByRole('tab', { name: /Welcome/, selected: true })).toBeVisible();
  });
});
