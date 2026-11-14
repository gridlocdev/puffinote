import { describe, expect, it } from 'vitest';
import { headingFor, parseLinkHref, resolveNoteLink, type NoteLink } from '../src/notebook/noteLinks.ts';
import { Notebook } from '../src/notebook/notebook.ts';
import { memoryPrefs } from '../src/notebook/settings.ts';
import { MemoryStore } from '../src/platform/memoryStore.ts';
import { ShellModel } from '../src/shell/shellModel.ts';

const files = [
  'Inbox.md',
  'notes/today.md',
  'notes/sub/other note.md',
  'notes/Plan.md',
  'archive/Plan.md',
  'archive/2024/Trip.md',
  'pics/a.png',
  'Ünïcode.md',
];

function resolve(from: string, href: string) {
  return resolveNoteLink(from, parseLinkHref(href) as Extract<NoteLink, { kind: 'file' | 'wiki' }>, files);
}

describe('links between notes', () => {
  it('reads what a link points at', () => {
    expect(parseLinkHref('https://example.com')).toEqual({ kind: 'external', url: 'https://example.com' });
    expect(parseLinkHref('MAILTO:a@b.c')).toEqual({ kind: 'external', url: 'MAILTO:a@b.c' });
    expect(parseLinkHref('sub/other%20note.md#Next%20steps')).toEqual({
      kind: 'file',
      ref: 'sub/other%20note.md',
      fragment: 'Next steps',
    });
    expect(parseLinkHref('#plan')).toEqual({ kind: 'heading', fragment: 'plan' });
    expect(parseLinkHref('[[Plan]]')).toEqual({ kind: 'wiki', target: 'Plan', fragment: '' });
    expect(parseLinkHref('[[notes/Plan|the plan]]')).toEqual({
      kind: 'wiki',
      target: 'notes/Plan',
      fragment: '',
    });
    expect(parseLinkHref('[[Plan#Goals|g]]')).toEqual({ kind: 'wiki', target: 'Plan', fragment: 'Goals' });
    expect(parseLinkHref('[[#Goals]]')).toEqual({ kind: 'heading', fragment: 'Goals' });
    for (const never of [
      'file:///etc/passwd',
      'javascript:alert(1)',
      'data:text/html,x',
      '//host/x',
      '',
      '[[]]',
    ]) {
      expect(parseLinkHref(never)).toBeNull();
    }
  });

  it('resolves a relative link against the note’s folder, decoded, falling back to another case', () => {
    expect(resolve('notes/today.md', 'sub/other%20note.md')).toEqual({
      kind: 'found',
      path: 'notes/sub/other note.md',
    });
    expect(resolve('notes/today.md', 'sub/other note.md')).toEqual({
      kind: 'found',
      path: 'notes/sub/other note.md',
    });
    expect(resolve('notes/today.md', '../Inbox.md#top')).toEqual({ kind: 'found', path: 'Inbox.md' });
    expect(resolve('notes/today.md', '/archive/2024/trip.md')).toEqual({
      kind: 'found',
      path: 'archive/2024/Trip.md',
    });
    expect(resolve('notes/today.md', 'Inbox.md')).toEqual({ kind: 'missing', name: 'Inbox.md' });
    expect(resolve('Inbox.md', '../../x.md')).toEqual({ kind: 'missing', name: '../../x.md' });
  });

  it('resolves a wiki link by path first, then by a unique name anywhere', () => {
    expect(resolve('notes/today.md', '[[Plan]]')).toEqual({ kind: 'found', path: 'notes/Plan.md' });
    expect(resolve('Inbox.md', '[[Plan]]')).toEqual({ kind: 'ambiguous', name: 'Plan' });
    expect(resolve('Inbox.md', '[[archive/Plan]]')).toEqual({ kind: 'found', path: 'archive/Plan.md' });
    expect(resolve('Inbox.md', '[[trip]]')).toEqual({ kind: 'found', path: 'archive/2024/Trip.md' });
    expect(resolve('Inbox.md', '[[2024/trip.md]]')).toEqual({ kind: 'found', path: 'archive/2024/Trip.md' });
    expect(resolve('Inbox.md', '[[other note]]')).toEqual({ kind: 'found', path: 'notes/sub/other note.md' });
    expect(resolve('Inbox.md', '[[a.png]]')).toEqual({ kind: 'found', path: 'pics/a.png' });
    expect(resolve('Inbox.md', '[[ünïcode]]')).toEqual({ kind: 'found', path: 'Ünïcode.md' });
    expect(resolve('Inbox.md', '[[Nowhere]]')).toEqual({ kind: 'missing', name: 'Nowhere' });
  });

  it('finds the heading a fragment names, as written or as an anchor', () => {
    const hs = [{ text: 'Intro' }, { text: 'Next steps!' }];
    expect(headingFor(hs, 'next-steps')).toBe(hs[1]);
    expect(headingFor(hs, 'Next steps')).toBe(hs[1]);
    expect(headingFor(hs, 'INTRO')).toBe(hs[0]);
    expect(headingFor(hs, 'outro')).toBeNull();
  });

  it('opens the linked note in a tab, at its heading, and says when it is missing', async () => {
    const nb = await Notebook.open(
      new MemoryStore('N', {
        'a.md': 'See [[b]] and [x](sub/c%20d.md#second).',
        'sub/c d.md': '# First\n\ntext\n\n## Second\n\nmore',
        'b.md': 'bee',
      }),
      { prefs: memoryPrefs() },
    );
    const m = new ShellModel();
    m.setNotebook(nb);
    await nb.openFile('a.md');
    await m.followLink('a.md', '[[b]]');
    expect(m.activeTab.value?.path.value).toBe('b.md');
    await m.followLink('a.md', 'sub/c%20d.md#second');
    expect(m.activeTab.value?.path.value).toBe('sub/c d.md');
    const state = m.activeState.value!;
    expect(state.selection.value.caret).toBe(state.text.indexOf('Second'));
    await m.followLink('sub/c d.md', '#first');
    expect(state.selection.value.caret).toBe(2);
    const tabs = nb.tabs.value.length;
    await m.followLink('a.md', '[[missing]]');
    expect(nb.notice.value?.text).toBe('There’s no “missing” in this notebook.');
    expect(nb.tabs.value.length).toBe(tabs);
    expect(nb.tree.value.children.some((c) => c.name.startsWith('missing'))).toBe(false);
  });
});
