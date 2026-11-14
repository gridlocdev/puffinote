import { TextDocument } from '@puffinote/text';
import { describe, expect, it } from 'vitest';
import { renderHtmlBody, renderHtmlDocument } from '../src/index.ts';

const body = (md: string): string => renderHtmlBody(TextDocument.fromString(md));

describe('HTML renderer', () => {
  it('renders headings', () => {
    expect(body('# Title')).toContain('<h1>Title</h1>');
    expect(body('### Sub')).toContain('<h3>Sub</h3>');
    expect(body('# Done #')).toContain('<h1>Done</h1>');
    expect(body('## Two ##   ')).toContain('<h2>Two</h2>');
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

  it('keeps spaces in a link or image path, encoded', () => {
    expect(body('[a](my file.md)')).toContain('<a href="my%20file.md">a</a>');
    expect(body('[a](<my file.md> "t")')).toContain('<a href="my%20file.md">a</a>');
    expect(body('![c](my cat.png)')).toContain('<img src="my%20cat.png" alt="c">');
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

  it('letters a nested numbered list and uses roman numerals a level further in', () => {
    const html = body('1. a\n    1. b\n        1. c');
    expect(html).toContain('<ol>');
    expect(html).toContain('<ol type="a">');
    expect(html).toContain('<ol type="i">');
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

  it('renders list and task items inside a blockquote as a list', () => {
    const html = body('> - [x] task\n> - plain');
    expect(html).toContain('<blockquote>\n<ul>\n<li class="task t-done">');
    expect(html).toContain('<li>plain</li>');
    expect(html).not.toContain('<p>- ');
    expect(body('> 1. one\n> 2. two')).toContain('<ol>\n<li>one</li>\n<li>two</li>\n</ol>');
  });

  it('escapes fenced code and keeps its language', () => {
    const html = body('```html\n<b>x</b>\n```');
    expect(html).toContain('class="language-html"');
    expect(html).toContain('&lt;b&gt;x&lt;/b&gt;');
  });

  it('leaves the closing fence out of a code block', () => {
    expect(body('```html\n<b>x</b>\n```')).toBe(
      '<pre><code class="language-html">&lt;b&gt;x&lt;/b&gt;\n</code></pre>\n',
    );
    expect(body('~~~\na\n~~~\n\nafter')).toBe('<pre><code>a\n</code></pre>\n<p>after</p>\n');
  });

  it('escapes quotes in a fence info string (§10.5)', () => {
    expect(body('```x" onmouseover="alert(1)\ncode\n```')).toBe(
      '<pre><code class="language-x&quot; onmouseover=&quot;alert(1)">code\n</code></pre>\n',
    );
  });

  it('drops links with an unsafe scheme, keeping the text (§10.5)', () => {
    const html = body(
      '[a](javascript:alert(1)) [b](JavaScript:x) [c](\u0001javascript:x) [d](vbscript:x) [e](data:text/html,x) [f](file:///etc)',
    );
    expect(html).toBe('<p>a b c d e f</p>\n');
    expect(body('<javascript:alert(2)>')).toBe('<p>javascript:alert(2)</p>\n');
  });

  it('keeps links to safe URLs (§10.5)', () => {
    expect(
      body('[h](http://a.com) [s](https://a.com) [m](mailto:a@b.c) [r](docs/x.md) [f](#top) [n](//host/x)'),
    ).toBe(
      '<p><a href="http://a.com">h</a> <a href="https://a.com">s</a> <a href="mailto:a@b.c">m</a> ' +
        '<a href="docs/x.md">r</a> <a href="#top">f</a> <a href="//host/x">n</a></p>\n',
    );
    expect(body('<https://a.com> <a@b.com>')).toBe(
      '<p><a href="https://a.com">https://a.com</a> <a href="mailto:a@b.com">a@b.com</a></p>\n',
    );
  });

  it('renders an image with an unsafe source as its alt text (§10.5)', () => {
    expect(body('![p](javascript:x) ![q](data:image/svg+xml,x)')).toBe('<p>p q</p>\n');
    expect(body('![i](data:image/png;base64,AAAA) ![j](pic.png)')).toBe(
      '<p><img src="data:image/png;base64,AAAA" alt="i"> <img src="pic.png" alt="j"></p>\n',
    );
  });

  it('keeps the text after an empty-alt image or an empty-URL link', () => {
    expect(body('![](pic.png) caption after')).toBe('<p><img src="pic.png" alt=""> caption after</p>\n');
    expect(body('[a]() tail text')).toBe('<p><a href="">a</a> tail text</p>\n');
  });

  it('renders a very deep blockquote without overflowing the stack', () => {
    const depth = 20000;
    const html = body('>'.repeat(depth) + ' deep');
    expect(html.split('<blockquote>').length - 1).toBe(depth);
    expect(html.split('</blockquote>').length - 1).toBe(depth);
    expect(html).toContain('<p>deep</p>');
  });

  it('finishes on whitespace-only lines inside and after indented code', () => {
    expect(body('    code\n    \n    more')).toContain('more');
    expect(body('para\n   \nnext')).toBe('<p>para</p>\n<p>next</p>\n');
    expect(body('\t\n  \n')).toBe('');
  });

  it('renders a horizontal rule', () => {
    expect(body('a\n\n---\n\nb')).toContain('<hr>');
  });

  it('keeps every line break in a paragraph as <br>', () => {
    expect(body('first  \nsecond')).toContain('first<br>\nsecond');
    expect(body('first\nsecond')).toBe('<p>first<br>\nsecond</p>\n');
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
