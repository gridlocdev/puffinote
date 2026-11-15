import { describe, expect, it } from 'vitest';
import { Tab } from '../src/notebook/tab.ts';

describe('Tab', () => {
  it('moves its generation with every change to what it knows of the disk', () => {
    const tab = new Tab('a.md', { kind: 'note', text: 'one', version: 'v1' });
    const seen = [tab.generation];
    tab.adoptVersion('v2');
    seen.push(tab.generation);
    tab.state!.insert(' two');
    tab.savedAs('v3', tab.state!.text);
    seen.push(tab.generation);
    tab.markSaved('v4');
    seen.push(tab.generation);
    expect(new Set(seen).size).toBe(4);
    expect(tab.version).toBe('v4');
  });

  it('adopting a version keeps the saved text, so unsaved edits stay unsaved', () => {
    const tab = new Tab('a.md', { kind: 'note', text: 'one', version: 'v1' });
    tab.state!.insert('!');
    tab.adoptVersion('v2');
    expect(tab.savedText).toBe('one');
    expect(tab.dirty.value).toBe(true);
  });

  it('a save leaves edits made while it was in flight unsaved', () => {
    const tab = new Tab('a.md', { kind: 'note', text: 'one', version: 'v1' });
    tab.state!.insert('!');
    const sent = tab.state!.text;
    tab.state!.insert('?');
    tab.savedAs('v2', sent);
    expect(tab.savedText).toBe(sent);
    expect(tab.dirty.value).toBe(true);
    expect(tab.conflict.value).toBe('none');
  });
});
