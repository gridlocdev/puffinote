import { describe, expect, it } from 'vitest';
import { hasScheme, leavesNotebook } from '../src/notebook/urls.ts';

describe('urls', () => {
  it('spots a scheme however it is cased, and only at the start', () => {
    for (const url of ['https://x', 'MAILTO:a@b', 'data:image/png;base64,', 'x-app+v1.2:go']) {
      expect(hasScheme(url)).toBe(true);
    }
    for (const url of ['notes/a.md', './a:b.md', '1http:x', '', '#top', '//host/x']) {
      expect(hasScheme(url)).toBe(false);
    }
  });

  it('treats a scheme or a //host as leaving the notebook, and a path or fragment as not', () => {
    expect(leavesNotebook('https://x')).toBe(true);
    expect(leavesNotebook('//cdn/x.png')).toBe(true);
    expect(leavesNotebook('pics/x.png')).toBe(false);
    expect(leavesNotebook('#top')).toBe(false);
  });
});
