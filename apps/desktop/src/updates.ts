import { readFile } from 'node:fs/promises';
import { writeAtomic } from './files.ts';

/** Where releases are published. The update check only reads this; it never downloads anything. */
export const RELEASES_API = 'https://api.github.com/repos/gridlocdev/Puffinote/releases/latest';
export const RELEASES_PAGE = 'https://github.com/gridlocdev/Puffinote/releases';
/** The automatic check runs at most this often. */
export const CHECK_INTERVAL_MS = 24 * 60 * 60 * 1000;

/** The newest published release. */
export interface Release {
  readonly version: string;
  readonly url: string;
}

/** What the app remembers about updates, in `updates.json` in its user data folder. */
export interface UpdatePrefs {
  /** Check once a day, on start. */
  readonly auto: boolean;
  /** A version the user chose to skip: the automatic check doesn't mention it again. */
  readonly skipped: string | null;
  /** When the last automatic check ran (ms since the epoch). */
  readonly lastCheck: number;
}

export const defaultUpdatePrefs: UpdatePrefs = { auto: true, skipped: null, lastCheck: 0 };

/** `v1.2.3` or `1.2.3` → [1, 2, 3]; null for anything else (pre-releases included). */
export function parseVersion(v: string): [number, number, number] | null {
  const m = /^v?(\d+)\.(\d+)\.(\d+)$/.exec(v.trim());
  return m ? [Number(m[1]), Number(m[2]), Number(m[3])] : null;
}

/** Is `latest` a newer version than `current`? False when either can't be read. */
export function isNewer(latest: string, current: string): boolean {
  const a = parseVersion(latest);
  const b = parseVersion(current);
  if (!a || !b) return false;
  for (let i = 0; i < 3; i++) if (a[i]! !== b[i]!) return a[i]! > b[i]!;
  return false;
}

/** The release from GitHub's `releases/latest` answer; its page must be on this repository's releases. */
export function parseRelease(json: unknown): Release | null {
  if (!json || typeof json !== 'object') return null;
  const { tag_name: tag, html_url: url, draft, prerelease } = json as Record<string, unknown>;
  if (typeof tag !== 'string' || !parseVersion(tag) || draft === true || prerelease === true) return null;
  const page = typeof url === 'string' && url.startsWith(`${RELEASES_PAGE}/`) ? url : RELEASES_PAGE;
  return { version: tag.replace(/^v/, ''), url: page };
}

/** Is an automatic check due? */
export function checkDue(prefs: UpdatePrefs, now: number): boolean {
  return prefs.auto && now - prefs.lastCheck >= CHECK_INTERVAL_MS;
}

export async function loadUpdatePrefs(file: string): Promise<UpdatePrefs> {
  try {
    const raw = JSON.parse(await readFile(file, 'utf8')) as Record<string, unknown>;
    return {
      auto: typeof raw.auto === 'boolean' ? raw.auto : defaultUpdatePrefs.auto,
      skipped: typeof raw.skipped === 'string' ? raw.skipped : null,
      lastCheck: typeof raw.lastCheck === 'number' ? raw.lastCheck : 0,
    };
  } catch {
    return defaultUpdatePrefs;
  }
}

export async function saveUpdatePrefs(file: string, prefs: UpdatePrefs): Promise<void> {
  await writeAtomic(file, new TextEncoder().encode(JSON.stringify(prefs, null, 2) + '\n'));
}

/** Ask GitHub for the newest release. Throws when it can't be reached or answers oddly. */
export async function fetchLatestRelease(fetcher: typeof fetch): Promise<Release> {
  const res = await fetcher(RELEASES_API, {
    headers: { Accept: 'application/vnd.github+json', 'User-Agent': 'Puffinote' },
    signal: AbortSignal.timeout(15_000),
  });
  if (!res.ok) throw new Error(`GitHub answered ${res.status}`);
  const release = parseRelease(await res.json());
  if (!release) throw new Error('No release found');
  return release;
}
