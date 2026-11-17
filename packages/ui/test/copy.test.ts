import { describe, expect, it } from 'vitest';
import { selectionHtml, showsPictures } from '../src/editor/copy.ts';

describe('formatted copy (PRD §10.1)', () => {
  it('renders the selection as formatted text', () => {
    expect(selectionHtml('a **b**\n\n- c')).toBe('<p>a <strong>b</strong></p>\n<ul>\n<li>c</li>\n</ul>\n');
  });

  it('knows when a selection shows a picture: a link or a wiki embed, not in code', () => {
    expect(showsPictures('x ![a](p.png) y')).toBe(true);
    expect(showsPictures('![[p.png]]')).toBe(true);
    expect(showsPictures('just text')).toBe(false);
    expect(showsPictures('`![a](p.png)`')).toBe(false);
    expect(showsPictures('[link](p.png)')).toBe(false);
  });
});
