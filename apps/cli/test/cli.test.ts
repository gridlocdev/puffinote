import {
  chmodSync,
  linkSync,
  lstatSync,
  statSync,
  mkdirSync,
  mkdtempSync,
  readdirSync,
  readFileSync,
  realpathSync,
  rmSync,
  symlinkSync,
  writeFileSync,
} from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import { runCli } from '../src/cli.ts';
import { countOccurrences, snippet } from '../src/commands/notebook.ts';

interface Run {
  exit: number;
  out: string;
  err: string;
  launched: string[];
}

function run(argv: string[], stdin = ''): Run {
  const r: Run = { exit: 0, out: '', err: '', launched: [] };
  r.exit = runCli(
    argv,
    {
      stdout: (t) => (r.out += t),
      stderr: (t) => (r.err += t),
      readStdin: () => new TextEncoder().encode(stdin),
      launch: (folder) => {
        r.launched.push(folder);
        return 0;
      },
    },
    '1.2.3',
  );
  return r;
}

let nb: string;
const at = (...parts: string[]) => path.join(nb, ...parts);

beforeAll(() => {
  nb = realpathSync(mkdtempSync(path.join(os.tmpdir(), 'puffinote-cli-')));
  mkdirSync(at('Sub', 'Deep'), { recursive: true });
  mkdirSync(at('.puffinote'));
  mkdirSync(at('apple'));
  writeFileSync(
    at('Welcome.md'),
    '# Welcome\n\nHello **world**, puffin here.\n\n## Puffins\n\nA puffin. Puffin!\n',
  );
  writeFileSync(at('note10.md'), 'ten\n');
  writeFileSync(at('note2.md'), 'two puffin\n');
  writeFileSync(at('image.png'), 'png');
  writeFileSync(at('.hidden.md'), 'puffin puffin puffin');
  writeFileSync(at('.puffinote', 'state.json'), '{}');
  writeFileSync(at('Sub', 'Deep', 'deep.md'), '### Deep [link](x) `code`\n');
  writeFileSync(
    at('latin.txt'),
    Buffer.from([0x63, 0x61, 0x66, 0xe9, 0x20, 0x70, 0x75, 0x66, 0x66, 0x69, 0x6e]),
  );
  symlinkSync(nb, at('Sub', 'loop'));
});
afterAll(() => rmSync(nb, { recursive: true, force: true }));

describe('tree', () => {
  it('lists folders first, in the sidebar order, without dotfiles, and stops at a link cycle', () => {
    const r = run(['tree', nb]);
    expect(r.exit).toBe(0);
    expect(r.out).toBe(
      [
        'apple/',
        'Sub/',
        '  Deep/',
        '    deep.md',
        '  loop/',
        'image.png',
        'latin.txt',
        'note2.md',
        'note10.md',
        'Welcome.md',
        '',
      ].join('\n'),
    );
  });

  it('reports a missing notebook', () => {
    const r = run(['tree', at('missing')]);
    expect(r.exit).toBe(1);
    expect(r.err).toBe(`puffinote: Notebook not found: ${at('missing')}\n`);
  });

  it('defaults to the current folder', () => {
    const cwd = process.cwd();
    process.chdir(at('Sub', 'Deep'));
    try {
      expect(run(['tree']).out).toBe('deep.md\n');
    } finally {
      process.chdir(cwd);
    }
  });
});

describe('search', () => {
  it('ranks notes by hits, then path, and skips hidden and non-note files', () => {
    const r = run(['search', 'PUFFIN', nb]);
    expect(r.exit).toBe(0);
    expect(r.out.split('\n').map((l) => l.split('\t').slice(0, 2))).toEqual([
      ['Welcome.md', '4'],
      ['latin.txt', '1'],
      ['note2.md', '1'],
      [''],
    ]);
    expect(r.out).toContain('latin.txt\t1\tcafé puffin\n');
  });

  it('limits the results with -n', () => {
    expect(run(['search', 'puffin', nb, '-n', '1']).out.split('\n')).toHaveLength(2);
    expect(run(['search', 'puffin', nb, '--max=0']).out).toBe('');
  });

  it('refuses an empty query and a bad --max', () => {
    expect(run(['search', '  ', nb]).err).toBe('puffinote: Search query must not be empty.\n');
    const bad = run(['search', 'x', nb, '-n', 'many']);
    expect(bad.exit).toBe(1);
    expect(bad.err).toContain("Cannot parse 'many'");
  });

  it('counts non-overlapping, case-insensitive matches', () => {
    expect(countOccurrences('aaaa', 'aa')).toBe(2);
    expect(countOccurrences('Puffin pUFFIN', 'puffin')).toBe(2);
    expect(countOccurrences('İstanbul istanbul', 'istanbul')).toBeGreaterThanOrEqual(1);
  });

  it('makes a one-line snippet with ellipses where it was cut', () => {
    const text = `${'x'.repeat(40)} needle\n\n  here ${'y'.repeat(80)}`;
    const s = snippet(text, 'NEEDLE');
    expect(s.startsWith('…')).toBe(true);
    expect(s.endsWith('…')).toBe(true);
    expect(s).toContain('needle here');
    expect(snippet('short needle', 'needle')).toBe('short needle');
  });
});

describe('info', () => {
  it('counts files, notes and bytes', () => {
    const r = run(['info', nb]);
    const rows = Object.fromEntries(
      r.out
        .trim()
        .split('\n')
        .map((l) => l.split('\t')),
    );
    expect(rows).toEqual({ notebook: nb, files: '6', notes: '5', other: '1', bytes: expect.any(String) });
    expect(Number(rows.bytes)).toBeGreaterThan(0);
  });
});

describe('markdown commands', () => {
  it('outline indents headings by level, without inline markup', () => {
    expect(run(['outline', at('Welcome.md')]).out).toBe('Welcome\n  Puffins\n');
    expect(run(['outline', at('Sub', 'Deep', 'deep.md')]).out).toBe('    Deep link code\n');
  });

  it('stats prints tab-separated counts', () => {
    expect(run(['stats', '-'], 'one two three\nfour').out).toBe(
      'words\t4\ncharacters\t18\nlines\t2\nreading_minutes\t1\n',
    );
  });

  it('tokens shows the stream with CR made visible and meta when present', () => {
    expect(run(['tokens', '-'], '# Hi\r\n').out).toBe(
      'headingMarker\tmarker\t[0..2]\t«# »\tmeta=1\ntext\tcontent\t[2..4]\t«Hi»\n',
    );
  });

  it('cat decodes the file the way the editor does', () => {
    expect(run(['cat', at('latin.txt')]).out).toBe('café puffin');
    expect(run(['cat', '-'], 'from stdin').out).toBe('from stdin');
  });

  it('html renders a document titled by the file name, or a fragment', () => {
    const doc = run(['html', at('Welcome.md')]).out;
    expect(doc.startsWith('<!DOCTYPE html>')).toBe(true);
    expect(doc).toContain('<title>Welcome</title>');
    expect(run(['html', '-'], '# A').out).toContain('<title>stdin</title>');
    expect(run(['html', '--fragment', '-'], '# A').out).toBe('<h1>A</h1>\n');
  });

  it('html -o writes the file in one step and leaves no temp file', () => {
    const out = at('Sub', 'out.html');
    const r = run(['html', at('Welcome.md'), '-o', out]);
    expect(r.exit).toBe(0);
    expect(r.out).toBe('');
    expect(readFileSync(out, 'utf8')).toContain('<h2>Puffins</h2>');
    expect(readdirSync(at('Sub')).filter((n) => n.endsWith('.tmp'))).toEqual([]);
    rmSync(out);
  });

  it('html -o through a symlink replaces the file it names and keeps its mode', () => {
    const target = at('Sub', 'target.html');
    const link = at('Sub', 'link.html');
    writeFileSync(target, 'old', { mode: 0o600 });
    chmodSync(target, 0o600);
    symlinkSync('target.html', link);
    expect(run(['html', at('Welcome.md'), '-o', link]).exit).toBe(0);
    expect(lstatSync(link).isSymbolicLink()).toBe(true);
    expect(readFileSync(target, 'utf8')).toContain('<h2>Puffins</h2>');
    // Windows has no POSIX permission bits.
    if (process.platform !== 'win32') expect(statSync(target).mode & 0o777).toBe(0o600);
    rmSync(link);
    rmSync(target);
  });

  it('html -o never writes over the note it renders', () => {
    const note = at('Sub', 'self.md');
    writeFileSync(note, '# Self\n');
    symlinkSync('self.md', at('Sub', 'self-link.md'));
    for (const out of [note, at('Sub', 'self-link.md'), at('Sub', '..', 'Sub', 'self.md')]) {
      const r = run(['html', note, '-o', out, '--force']);
      expect(r.exit).toBe(1);
      expect(r.err).toMatch(/^puffinote: .*same file/);
    }
    expect(readFileSync(note, 'utf8')).toBe('# Self\n');
    rmSync(at('Sub', 'self-link.md'));
    rmSync(note);
  });

  it('html -o writes only .html or .htm unless --force', () => {
    const other = at('Sub', 'other.md');
    writeFileSync(other, 'keep');
    const r = run(['html', at('Welcome.md'), '-o', other]);
    expect(r.exit).toBe(1);
    expect(r.err).toMatch(/^puffinote: .*\.html.*--force/);
    expect(readFileSync(other, 'utf8')).toBe('keep');
    expect(run(['html', at('Welcome.md'), '-o', other, '--force']).exit).toBe(0);
    expect(readFileSync(other, 'utf8')).toContain('<h2>Puffins</h2>');
    expect(run(['html', at('Welcome.md'), '-o', at('Sub', 'page.HTM')]).exit).toBe(0);
    rmSync(other);
    rmSync(at('Sub', 'page.HTM'));
  });

  it('html -o refuses to replace a read-only file', () => {
    const out = at('Sub', 'locked.html');
    writeFileSync(out, 'locked');
    chmodSync(out, 0o444);
    const r = run(['html', at('Welcome.md'), '-o', out]);
    expect(r.exit).toBe(1);
    expect(r.err).toMatch(/^puffinote: .*read-only/);
    expect(readFileSync(out, 'utf8')).toBe('locked');
    chmodSync(out, 0o644);
    rmSync(out);
  });

  it('html -o writes through a hard link instead of splitting it', () => {
    const out = at('Sub', 'hard.html');
    writeFileSync(out, 'old');
    linkSync(out, at('Sub', 'hard-too.html'));
    expect(run(['html', at('Welcome.md'), '-o', out]).exit).toBe(0);
    expect(readFileSync(at('Sub', 'hard-too.html'), 'utf8')).toContain('<h2>Puffins</h2>');
    rmSync(out);
    rmSync(at('Sub', 'hard-too.html'));
  });

  it('reports a missing file', () => {
    const r = run(['stats', at('nope.md')]);
    expect(r.exit).toBe(1);
    expect(r.err).toBe(`puffinote: File not found: ${at('nope.md')}\n`);
  });
});

describe('the root command', () => {
  it('prints help when bare, and the version', () => {
    const r = run([]);
    expect(r.exit).toBe(0);
    expect(r.out).toContain('Usage:\n  puffinote [<path>] [command] [options]');
    expect(r.out).toContain('search <query> [<notebook>]');
    expect(run(['--version']).out).toBe('1.2.3\n');
  });

  it('opens a folder in the app, and a file by the folder it is in', () => {
    expect(run([nb]).launched).toEqual([nb]);
    expect(run([at('Sub', 'Deep', 'deep.md')]).launched).toEqual([at('Sub', 'Deep')]);
    const missing = run([at('nope')]);
    expect(missing.exit).toBe(1);
    expect(missing.launched).toEqual([]);
  });

  it('answers a parse error with the message on stderr and the help on stdout', () => {
    const r = run(['html', '--bogus', 'x.md']);
    expect(r.exit).toBe(1);
    expect(r.err).toBe("Unrecognized command or argument '--bogus'.\n\n");
    expect(r.out).toContain('puffinote html <file> [options]');
    expect(run(['outline']).err).toContain('Required argument missing');
  });
});
