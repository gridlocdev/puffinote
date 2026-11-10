import { TextDocument } from '@puffinote/text';
import { describe, expect, it } from 'vitest';
import { renderHtmlBody, renderHtmlDocument } from '../src/index.ts';

const body = (md: string): string => renderHtmlBody(TextDocument.fromString(md));

describe('HTML renderer', () => {
  it('renders headings', () => {
    expect(body('# Title')).toContain('<h1>Title</h1>');
    expect(body('### Sub')).toContain('<h3>Sub</h3>');
  });

  it('renders inline constructs in a paragraph', () => {
    const html = body('a **b** and *c* and `d`');
    expect(html).toContain('<strong>b</strong>');
    expect(html).toContain('<em>c</em>');
    expect(html).toContain('<code>d</code>');
  });

  it('renders links and images', () => {
    expect(body('[home](https://x.com)')).toContain('<a href="https://x.com">home</a>');
    expect(body('![cat](p.png)')).toContain('<img src="p.png" alt="cat">');
  });

  it('renders unordered and ordered lists', () => {
    const html = body('- one\n- two');
    expect(html).toContain('<ul>');
    expect(html).toContain('<li>one</li>');
    expect(html).toContain('<li>two</li>');
    expect(body('1. one\n2. two')).toContain('<ol>');
    expect(body('3. three\n4. four')).toContain('<ol start="3">');
  });

  it('nests a deeper list inside the preceding item', () => {
    const html = body('- a\n  - b\n  - c\n- d');
    const firstItem = html.indexOf('<li>a');
    const nested = html.indexOf('<ul>', firstItem);
    const firstClose = html.indexOf('</li>', firstItem);
    expect(nested).toBeGreaterThan(firstItem);
    expect(nested).toBeLessThan(firstClose);
    expect(html).toContain('<li>b</li>');
    expect(html).toContain('<li>d</li>');
  });

  it('keeps a loose list as one list across a blank line', () => {
    expect(body('1. one\n\n2. two').match(/<ol/g)).toHaveLength(1);
  });

  it('renders task boxes with a state class and label', () => {
    const html = body('- [ ] todo\n- [x] done');
    expect(html).toContain('<li class="task t-not">');
    expect(html).toContain('<li class="task t-done">');
    expect(html).toContain('aria-label="Not started"');
    expect(html).toContain('aria-label="Done"');
    expect(html).toContain('<span class="ttext">todo</span>');
  });

  it('renders every task state, drawing a symbol only for the lit ones', () => {
    const html = body('- [ ] a\n- [/] b\n- [x] c');
    for (const [cls, label] of [
      ['t-not', 'Not started'],
      ['t-prog', 'In progress'],
      ['t-done', 'Done'],
    ]) {
      expect(html).toContain(`<li class="task ${cls}">`);
      expect(html).toContain(`aria-label="${label}"`);
    }
    expect(html.match(/<svg/g)).toHaveLength(2);
  });

  it('gives the two lit states one accent', () => {
    const page = renderHtmlDocument(TextDocument.fromString('- [/] b\n- [x] c'));
    expect(page).toContain('li.t-prog > .tbox { color: var(--t-accent); }');
    expect(page).toContain('background: var(--t-accent); border-color: var(--t-accent)');
  });

  it('renders blockquotes, nesting deeper ones', () => {
    expect(body('> quoted')).toContain('<blockquote>');
    const html = body('> outer\n>> inner\n');
    const outer = html.indexOf('<blockquote>');
    const inner = html.indexOf('<blockquote>', outer + 1);
    expect(inner).toBeGreaterThan(0);
    expect(html.indexOf('inner')).toBeGreaterThan(inner);
  });

  it('escapes fenced code and keeps its language', () => {
    const html = body('```html\n<b>x</b>\n```');
    expect(html).toContain('class="language-html"');
    expect(html).toContain('&lt;b&gt;x&lt;/b&gt;');
  });

  it('renders a horizontal rule', () => {
    expect(body('a\n\n---\n\nb')).toContain('<hr>');
  });

  it('renders a hard break as <br> and a soft wrap as a newline', () => {
    expect(body('first  \nsecond')).toContain('first<br>\nsecond');
    const soft = body('first\nsecond');
    expect(soft).toContain('first\nsecond');
    expect(soft).not.toContain('<br>');
  });

  it('renders a table', () => {
    const html = body('| A | B |\n| - | - |\n| 1 | 2 |');
    expect(html).toContain('<table>');
    expect(html).toContain('<th>A</th>');
    expect(html).toContain('<td>1</td>');
  });

  it('escapes HTML in text', () => {
    expect(body('a <script> b')).toContain('a &lt;script&gt; b');
  });

  it('renders front matter as properties', () => {
    const html = body('---\ntitle: Hi\n---\n# Body');
    expect(html).toContain('<details class="properties">');
    expect(html).toContain('<h1>Body</h1>');
  });

  it('renders setext headings, and the underline emits nothing', () => {
    const html = body('One\n===\n\nTwo\n---\n');
    expect(html).toContain('<h1>One</h1>');
    expect(html).toContain('<h2>Two</h2>');
    expect(html).not.toContain('<hr>');
    expect(html).not.toContain('<p></p>');
  });

  it('renders a self-contained document', () => {
    const page = renderHtmlDocument(TextDocument.fromString('# Hi'), 'My Doc');
    expect(page.startsWith('<!DOCTYPE html>')).toBe(true);
    expect(page).toContain('<title>My Doc</title>');
    expect(page).toContain('<style>');
    expect(page).toContain('<h1>Hi</h1>');
  });
});
