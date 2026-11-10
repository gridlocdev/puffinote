import { mkdtemp, readFile, rm, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { afterAll, describe, expect, it } from 'vitest';
import {
  CHECK_INTERVAL_MS,
  checkDue,
  defaultUpdatePrefs,
  fetchLatestRelease,
  isNewer,
  loadUpdatePrefs,
  parseRelease,
  RELEASES_PAGE,
  saveUpdatePrefs,
} from '../src/updates.ts';

describe('update check', () => {
  it('compares versions number by number', () => {
    expect(isNewer('v1.0.1', '1.0.0')).toBe(true);
    expect(isNewer('1.10.0', '1.9.9')).toBe(true);
    expect(isNewer('2.0.0', '1.99.99')).toBe(true);
    expect(isNewer('v1.0.0', '1.0.0')).toBe(false);
    expect(isNewer('0.9.0', '1.0.0')).toBe(false);
    expect(isNewer('1.1.0-beta.1', '1.0.0')).toBe(false);
    expect(isNewer('nonsense', '1.0.0')).toBe(false);
  });

  it('reads a release, and keeps its link on this repository', () => {
    expect(parseRelease({ tag_name: 'v1.2.0', html_url: `${RELEASES_PAGE}/tag/v1.2.0` })).toEqual({
      version: '1.2.0',
      url: `${RELEASES_PAGE}/tag/v1.2.0`,
    });
    expect(parseRelease({ tag_name: 'v1.2.0', html_url: 'https://evil.example/x' })?.url).toBe(RELEASES_PAGE);
    expect(parseRelease({ tag_name: 'v1.2.0', prerelease: true })).toBeNull();
    expect(parseRelease({ tag_name: 'latest' })).toBeNull();
    expect(parseRelease(null)).toBeNull();
  });

  it('checks automatically once a day, and never when turned off', () => {
    const now = 10 * CHECK_INTERVAL_MS;
    expect(checkDue(defaultUpdatePrefs, now)).toBe(true);
    expect(checkDue({ ...defaultUpdatePrefs, lastCheck: now - 1000 }, now)).toBe(false);
    expect(checkDue({ ...defaultUpdatePrefs, lastCheck: now - CHECK_INTERVAL_MS }, now)).toBe(true);
    expect(checkDue({ ...defaultUpdatePrefs, auto: false }, now)).toBe(false);
  });

  it('fetches the latest release, and fails on a bad answer', async () => {
    const ok = (async () =>
      new Response(
        JSON.stringify({ tag_name: 'v3.1.4', html_url: `${RELEASES_PAGE}/tag/v3.1.4` }),
      )) as typeof fetch;
    await expect(fetchLatestRelease(ok)).resolves.toEqual({
      version: '3.1.4',
      url: `${RELEASES_PAGE}/tag/v3.1.4`,
    });
    const missing = (async () => new Response('{}', { status: 404 })) as typeof fetch;
    await expect(fetchLatestRelease(missing)).rejects.toThrow('404');
  });

  const dir = mkdtemp(path.join(os.tmpdir(), 'pn-updates-'));
  afterAll(async () => rm(await dir, { recursive: true, force: true }));

  it('remembers its preferences, and survives a broken file', async () => {
    const file = path.join(await dir, 'updates.json');
    expect(await loadUpdatePrefs(file)).toEqual(defaultUpdatePrefs);
    await saveUpdatePrefs(file, { auto: false, skipped: '1.1.0', lastCheck: 42 });
    expect(JSON.parse(await readFile(file, 'utf8'))).toEqual({
      auto: false,
      skipped: '1.1.0',
      lastCheck: 42,
    });
    expect(await loadUpdatePrefs(file)).toEqual({ auto: false, skipped: '1.1.0', lastCheck: 42 });
    await writeFile(file, '{ not json');
    expect(await loadUpdatePrefs(file)).toEqual(defaultUpdatePrefs);
  });
});
