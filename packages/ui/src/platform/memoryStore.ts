import {
  ConflictError,
  NotFoundError,
  type Entry,
  type FileData,
  type NotebookStore,
  type WriteOptions,
} from './notebookStore.ts';
import { baseName, isWithin, parentOf, rebase } from './paths.ts';

/**
 * A notebook held in memory: the demo, tests, and a scratch notebook when nothing else is available. Versions
 * are a counter, so conflict handling behaves as it does against a real folder; `touch` simulates an edit
 * made outside the app.
 */
export class MemoryStore implements NotebookStore {
  readonly kind = 'memory' as const;
  readonly name: string;
  readonly capabilities = { watch: true, reveal: false, openExternal: false };
  private readonly files = new Map<string, { bytes: Uint8Array; version: number; modified: number }>();
  private readonly dirs = new Set<string>(['']);
  private readonly listeners = new Set<(paths: readonly string[]) => void>();
  private counter = 0;

  constructor(name = 'Notebook', files: Record<string, string | Uint8Array> = {}) {
    this.name = name;
    for (const [path, content] of Object.entries(files)) {
      this.put(path, typeof content === 'string' ? new TextEncoder().encode(content) : content);
    }
  }

  list(dir: string): Promise<Entry[]> {
    if (!this.dirs.has(dir)) return Promise.reject(new NotFoundError(dir));
    const out: Entry[] = [];
    for (const d of this.dirs) {
      if (d !== '' && parentOf(d) === dir)
        out.push({ path: d, name: baseName(d), kind: 'dir', size: 0, modified: 0 });
    }
    for (const [path, f] of this.files) {
      if (parentOf(path) === dir) {
        out.push({ path, name: baseName(path), kind: 'file', size: f.bytes.length, modified: f.modified });
      }
    }
    return Promise.resolve(out);
  }

  read(path: string): Promise<FileData> {
    const f = this.files.get(path);
    if (!f) return Promise.reject(new NotFoundError(path));
    return Promise.resolve({ bytes: f.bytes.slice(), version: String(f.version) });
  }

  write(path: string, bytes: Uint8Array, opts: WriteOptions = {}): Promise<string | null> {
    const existing = this.files.get(path);
    if (opts.create && (existing || this.dirs.has(path))) {
      return Promise.reject(new ConflictError('exists', `“${baseName(path)}” already exists.`));
    }
    if (!opts.force && opts.expected != null) {
      if (!existing) {
        return Promise.reject(new ConflictError('deleted', 'The file was deleted since it was opened.'));
      }
      if (String(existing.version) !== opts.expected) {
        return Promise.reject(new ConflictError('changed', 'The file changed since it was opened.'));
      }
    }
    return Promise.resolve(String(this.put(path, bytes.slice())));
  }

  mkdir(path: string): Promise<void> {
    if (this.files.has(path))
      return Promise.reject(new ConflictError('exists', `“${baseName(path)}” is a file.`));
    this.ensureDir(path);
    return Promise.resolve();
  }

  move(from: string, to: string): Promise<void> {
    if (from === to) return Promise.resolve();
    if (this.files.has(to) || this.dirs.has(to)) {
      return Promise.reject(new ConflictError('exists', `“${baseName(to)}” already exists there.`));
    }
    const file = this.files.get(from);
    if (file) {
      this.files.delete(from);
      this.ensureDir(parentOf(to));
      this.files.set(to, file);
      return Promise.resolve();
    }
    if (!this.dirs.has(from)) return Promise.reject(new NotFoundError(from));
    if (isWithin(to, from)) return Promise.reject(new Error('A folder can’t move into itself.'));
    this.ensureDir(parentOf(to));
    for (const d of [...this.dirs]) {
      if (isWithin(d, from)) {
        this.dirs.delete(d);
        this.dirs.add(rebase(d, from, to));
      }
    }
    for (const [p, f] of [...this.files]) {
      if (isWithin(p, from)) {
        this.files.delete(p);
        this.files.set(rebase(p, from, to), f);
      }
    }
    return Promise.resolve();
  }

  remove(path: string): Promise<void> {
    if (this.files.delete(path)) return Promise.resolve();
    if (!this.dirs.has(path) || path === '') return Promise.reject(new NotFoundError(path));
    for (const d of [...this.dirs]) if (isWithin(d, path)) this.dirs.delete(d);
    for (const p of [...this.files.keys()]) if (isWithin(p, path)) this.files.delete(p);
    return Promise.resolve();
  }

  fileUrl(): string | null {
    return null;
  }

  watch(onChange: (paths: readonly string[]) => void): () => void {
    this.listeners.add(onChange);
    return () => this.listeners.delete(onChange);
  }

  /** Change a file as another program would, and tell the watchers. */
  touch(path: string, content: string | null): void {
    if (content === null) this.files.delete(path);
    else this.put(path, new TextEncoder().encode(content));
    for (const l of this.listeners) l([path]);
  }

  private put(path: string, bytes: Uint8Array): number {
    this.ensureDir(parentOf(path));
    const version = ++this.counter;
    this.files.set(path, { bytes, version, modified: Date.now() });
    return version;
  }

  private ensureDir(path: string): void {
    for (let p = path; p !== '' && !this.dirs.has(p); p = parentOf(p)) this.dirs.add(p);
  }
}
