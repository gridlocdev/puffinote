import {
  ConflictError,
  NotFoundError,
  type Entry,
  type FileData,
  type NotebookStore,
  type WriteOptions,
} from './notebookStore.ts';
import { baseName } from './paths.ts';

export interface NasRoot {
  readonly id: string;
  readonly name: string;
}
export interface NasInfo {
  readonly name: string;
  readonly auth: boolean;
}

/** Needs a token, or the token is wrong. */
export class AuthError extends Error {}

const TOKEN_KEY = 'puffinote.nasToken';

/**
 * The NAS server's API (`/api`), shared by the folder browser that picks a notebook and the store that edits
 * one. Conflict detection uses etags: a save sends If-Match, and the server answers 412 if the file changed.
 */
export class NasClient {
  readonly base: string;
  private readonly fetchImpl: typeof fetch;

  constructor(base = './api', fetchImpl: typeof fetch = globalThis.fetch.bind(globalThis)) {
    this.base = base.replace(/\/$/, '');
    this.fetchImpl = fetchImpl;
  }

  get token(): string | null {
    try {
      return localStorage.getItem(TOKEN_KEY);
    } catch {
      return null;
    }
  }

  set token(value: string | null) {
    try {
      if (value) localStorage.setItem(TOKEN_KEY, value);
      else localStorage.removeItem(TOKEN_KEY);
    } catch {
      // Storage disabled: the token lasts only for this page.
    }
  }

  /** Null when no server answers (a static deployment, or file://). */
  async info(): Promise<NasInfo | null> {
    try {
      const res = await this.fetchImpl(`${this.base}/info`, { headers: this.headers() });
      if (!res.ok || !res.headers.get('content-type')?.includes('json')) return null;
      return (await res.json()) as NasInfo;
    } catch {
      return null;
    }
  }

  async roots(): Promise<NasRoot[]> {
    return (await (await this.request('/roots')).json()) as NasRoot[];
  }

  async list(root: string, path: string): Promise<Entry[]> {
    const res = await this.request(`/list?${new URLSearchParams({ root, path })}`);
    return (await res.json()) as Entry[];
  }

  async request(path: string, init: RequestInit = {}): Promise<Response> {
    const res = await this.fetchImpl(this.base + path, {
      ...init,
      headers: { ...this.headers(), ...(init.headers as Record<string, string> | undefined) },
    });
    if (res.ok) return res;
    const message = await res
      .json()
      .then((b: { error?: string }) => b.error ?? res.statusText)
      .catch(() => res.statusText);
    if (res.status === 401) throw new AuthError(message);
    if (res.status === 409 || (res.status === 412 && message.includes('exists'))) {
      throw new ConflictError('exists', message);
    }
    if (res.status === 412) throw new ConflictError('changed', message);
    if (res.status === 423) throw new ConflictError('locked', message);
    if (res.status === 404) throw new NotFoundError(path);
    throw new Error(message);
  }

  headers(): Record<string, string> {
    const t = this.token;
    return t ? { Authorization: `Bearer ${t}` } : {};
  }
}

/**
 * A notebook in a NAS folder: the folder `base` inside the server root `root`. Notebook paths map to
 * `base/path` on the server.
 */
export class NasStore implements NotebookStore {
  readonly kind = 'nas' as const;
  readonly name: string;
  readonly capabilities = { watch: false, reveal: false, openExternal: false };
  readonly client: NasClient;
  readonly root: string;
  readonly base: string;

  constructor(client: NasClient, root: string, base: string, name?: string) {
    this.client = client;
    this.root = root;
    this.base = base.replace(/^\/|\/$/g, '');
    this.name = name ?? (baseName(this.base) || root);
  }

  async list(dir: string): Promise<Entry[]> {
    const entries = await this.client.list(this.root, this.full(dir));
    return entries.map((e) => ({ ...e, path: this.rel(e.path) }));
  }

  async read(path: string): Promise<FileData> {
    const res = await this.client.request(`/file?${this.query({ path: this.full(path) })}`);
    return { bytes: new Uint8Array(await res.arrayBuffer()), version: res.headers.get('etag') };
  }

  async write(path: string, bytes: Uint8Array, opts: WriteOptions = {}): Promise<string | null> {
    const headers: Record<string, string> = { 'Content-Type': 'application/octet-stream' };
    if (opts.create) headers['If-None-Match'] = '*';
    else if (!opts.force && opts.expected != null) headers['If-Match'] = opts.expected;
    const res = await this.client.request(`/file?${this.query({ path: this.full(path) })}`, {
      method: 'PUT',
      body: bytes as Uint8Array<ArrayBuffer>,
      headers,
    });
    const { etag } = (await res.json()) as { etag: string | null };
    return etag;
  }

  async mkdir(path: string): Promise<void> {
    await this.client.request(`/mkdir?${this.query({ path: this.full(path) })}`, { method: 'POST' });
  }

  async move(from: string, to: string): Promise<void> {
    const q = this.query({ from: this.full(from), to: this.full(to) });
    await this.client.request(`/move?${q}`, { method: 'POST' });
  }

  async remove(path: string): Promise<void> {
    await this.client.request(`/file?${this.query({ path: this.full(path) })}`, { method: 'DELETE' });
  }

  /** Images load straight from the server, unless it needs a token (an <img> can't send one). */
  fileUrl(path: string): string | null {
    if (this.client.token) return null;
    return `${this.client.base}/file?${this.query({ path: this.full(path) })}`;
  }

  private query(params: Record<string, string>): URLSearchParams {
    return new URLSearchParams({ root: this.root, ...params });
  }

  private full(path: string): string {
    return this.base && path ? `${this.base}/${path}` : this.base || path;
  }

  private rel(serverPath: string): string {
    const p = serverPath.replace(/^\//, '');
    return this.base && p.startsWith(this.base + '/') ? p.slice(this.base.length + 1) : p;
  }
}
