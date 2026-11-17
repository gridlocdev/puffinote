import { editor, expect, letAutosave, openApp, openNote, readData, runCommand, test } from './fixtures.ts';

test.describe('Formatting as you type', () => {
  test.beforeEach(async ({ seed, page }) => {
    seed('basic');
    await openApp(page);
    await openNote(page, 'welcome');
  });

  test('Raw shows the Markdown markers that Rich hides', async ({ page }) => {
    const view = page.getByRole('group', { name: 'View' });
    const marked = page.getByRole('main').getByText('**made up**');
    await expect(marked).toBeHidden();
    await view.getByRole('button', { name: 'Raw' }).click();
    await expect(marked).toBeVisible();
    await view.getByRole('button', { name: 'Rich' }).click();
    await expect(marked).toBeHidden();
  });

  test('Enter at the end of a task starts the next task', async ({ page }) => {
    await page.getByRole('main').getByText('Water the ferns').click();
    await page.keyboard.press('End');
    await page.keyboard.press('Enter');
    await page.keyboard.type('Buy stamps');
    await letAutosave(page);
    await expect.poll(() => readData('Welcome.md')).toContain('- [ ] Water the ferns\n- [ ] Buy stamps\n');
    await expect(editor(page)).toBeFocused();
  });

  test('⌘Z in a dialog’s empty box leaves the note behind it alone', async ({ page }) => {
    await page.keyboard.type('x');
    await runCommand(page, 'New Folder…');
    const name = page.getByRole('dialog', { name: 'New Folder' }).getByRole('textbox');
    await expect(name).toBeFocused();
    await page.keyboard.press('ControlOrMeta+KeyZ');
    await expect(name).toBeFocused();
    await page.keyboard.press('Escape');
    await letAutosave(page);
    await expect.poll(() => readData('Welcome.md')).toMatch(/^x/);
  });
});

test.describe('Tasks with three states', () => {
  test.beforeEach(async ({ seed, page }) => {
    seed('basic');
    await openApp(page);
    await openNote(page, 'welcome');
  });

  test('a click on the box ticks the task and moves it below the open ones', async ({ page }) => {
    await page.getByRole('checkbox', { name: 'Water the ferns, not done' }).click();
    await expect(page.getByRole('checkbox', { name: 'Water the ferns, done' })).toBeChecked();
    await letAutosave(page);
    await expect
      .poll(() => readData('Welcome.md'))
      .toContain('- [/] Sort the receipts\n- [x] Water the ferns\n- [x] Call the plumber\n');
  });
});

test.describe('Pictures in your notes', () => {
  test('a picture in a note is drawn', async ({ seed, page }) => {
    seed('basic');
    await openApp(page);
    await openNote(page, 'packing');
    // The editor draws it with an empty alt, so find it by its file.
    const picture = page.getByRole('main').locator('img[src*="square.png"]');
    await expect(picture).toBeVisible();
    expect(await picture.evaluate((img: HTMLImageElement) => img.naturalWidth)).toBe(16);
  });

  test('copying text with a picture puts the picture itself on the clipboard, not its link', async ({
    seed,
    page,
  }) => {
    seed('basic');
    await page.context().grantPermissions(['clipboard-read', 'clipboard-write']);
    await openApp(page);
    await openNote(page, 'packing');
    await page.getByRole('main').getByText('Boots').click();
    await page.keyboard.press('ControlOrMeta+KeyA');
    await page.keyboard.press('ControlOrMeta+KeyC');
    const read = () =>
      page.evaluate(async () => {
        // Read again while the pictures are still being written: the clipboard changes under the first read.
        const [item] = await navigator.clipboard.read().catch(() => []);
        if (!item?.types.includes('text/html')) return { html: '', text: '' };
        try {
          const text = item.types.includes('text/plain')
            ? await (await item.getType('text/plain')).text()
            : '';
          return { html: await (await item.getType('text/html')).text(), text };
        } catch {
          return { html: '', text: '' };
        }
      });
    let copied = { html: '', text: '' };
    await expect.poll(async () => (copied = await read()).html).toContain('<img src="data:image/png;base64,');
    const { html, text } = copied;
    expect(html).toContain('<li>Boots</li>');
    expect(html).not.toContain('src="square.png"');
    expect(text).toContain('![A small orange square](square.png)');
  });
});
