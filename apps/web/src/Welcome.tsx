import { useSignal } from '@preact/signals';
import { useEffect } from 'preact/hooks';
import { canPickFolder, Icon, type Entry, type NasClient, type NasRoot } from '@puffinote/ui';
import { recentKey, recentPlace, type Launcher, type Recent } from './launcher.ts';

/** What the window shows with no notebook open: recent notebooks and the ways to open one. */
export function Welcome(props: { readonly launcher: Launcher }) {
  const { launcher } = props;
  const recents = launcher.available();
  const opening = launcher.opening.value;
  // While start-up reopens a notebook, show only that, so the welcome screen doesn't flash past.
  if (launcher.starting.value) {
    return (
      <div class="pw-welcome">
        <p class="pw-opening">{opening ? `Opening “${opening}”…` : 'Starting…'}</p>
      </div>
    );
  }
  return (
    <div class="pw-welcome">
      <header class="pw-hero">
        <h1>Puffinote</h1>
        <p>A notebook is a folder of Markdown notes.</p>
      </header>
      {launcher.error.value && (
        <p class="pw-error" role="alert">
          {launcher.error.value}
        </p>
      )}
      {opening && <p class="pw-opening">Opening “{opening}”…</p>}
      {recents.length > 0 && (
        <section class="pw-section" aria-label="Recent notebooks">
          <h2>Recent</h2>
          <ul class="pw-list">
            {recents.map((r) => (
              <RecentRow key={recentKey(r)} launcher={launcher} recent={r} />
            ))}
          </ul>
        </section>
      )}
      {launcher.kind === 'nas' && launcher.nas ? (
        launcher.needsToken.value ? (
          <TokenForm launcher={launcher} />
        ) : (
          <NasBrowser launcher={launcher} client={launcher.nas} />
        )
      ) : (
        <LocalActions launcher={launcher} />
      )}
    </div>
  );
}

function RecentRow(props: { readonly launcher: Launcher; readonly recent: Recent }) {
  const { launcher, recent } = props;
  return (
    <li class="pw-row">
      <button class="pw-row-main" onClick={() => void launcher.open(recent)}>
        <Icon name="book-open" />
        <span class="pw-row-text">
          <span class="pw-row-name">{recent.name}</span>
          <span class="pw-row-place">{recentPlace(recent)}</span>
        </span>
      </button>
      <button
        class="pn-icon-button pw-row-forget"
        title="Remove from Recent"
        aria-label={`Remove ${recent.name} from Recent`}
        onClick={() => launcher.forget(recent)}
      >
        <Icon name="x" />
      </button>
    </li>
  );
}

/**
 * Where a folder can be opened (the desktop app, Chromium), that is the one action. Elsewhere the browser
 * notebook opens on start, so this shows only after it was closed or couldn't open (PRD §3).
 */
function LocalActions(props: { readonly launcher: Launcher }) {
  const { launcher } = props;
  if (launcher.kind === 'native' || canPickFolder()) {
    return (
      <section class="pw-section pw-actions" aria-label="Open a notebook">
        <button class="pw-primary" onClick={() => void launcher.openFolder()}>
          <Icon name="folder-open" />
          Open Folder…
        </button>
      </section>
    );
  }
  return (
    <section class="pw-section pw-actions" aria-label="Open a notebook">
      <button class="pw-primary" onClick={() => void launcher.openPrivate()}>
        <Icon name="book-open" />
        Open the Browser Notebook
      </button>
      <p class="pw-note">Notes are kept in this browser.</p>
    </section>
  );
}

function TokenForm(props: { readonly launcher: Launcher }) {
  const value = useSignal('');
  return (
    <form
      class="pw-section pw-token"
      onSubmit={(e) => {
        e.preventDefault();
        void props.launcher.setToken(value.value);
      }}
    >
      <h2>Sign in to the server</h2>
      <label class="pn-field">
        <span>Access token</span>
        <input
          type="password"
          autocomplete="current-password"
          value={value.value}
          onInput={(e) => (value.value = (e.target as HTMLInputElement).value)}
          autoFocus
        />
      </label>
      <p class="pw-note">The token is the PUFFINOTE_TOKEN the server was started with.</p>
      <button class="pw-primary" type="submit" disabled={value.value.trim() === ''}>
        Continue
      </button>
    </form>
  );
}

/** Pick a notebook on the NAS: a root, or a folder inside it. With one root, start-up opens it instead. */
function NasBrowser(props: { readonly launcher: Launcher; readonly client: NasClient }) {
  const { launcher, client } = props;
  const roots = useSignal<NasRoot[] | null>(null);
  const root = useSignal<NasRoot | null>(null);
  const path = useSignal('');
  const entries = useSignal<Entry[] | null>(null);

  useEffect(() => {
    client.roots().then(
      (r) => {
        roots.value = r;
        if (r.length === 1) root.value = r[0]!;
      },
      (e: unknown) => launcher.reportError(e),
    );
  }, [client]);

  useEffect(() => {
    const r = root.value;
    if (!r) return;
    entries.value = null;
    let live = true;
    client.list(r.id, path.value).then(
      (list) => live && (entries.value = list.filter((e) => e.kind === 'dir' && !e.name.startsWith('.'))),
      (e: unknown) => live && launcher.reportError(e),
    );
    return () => {
      live = false;
    };
  }, [root.value, path.value]);

  if (!roots.value) return <p class="pw-note">Looking for notebooks…</p>;
  if (!root.value) {
    return (
      <section class="pw-section" aria-label="Server folders">
        <h2>On this server</h2>
        <ul class="pw-list">
          {roots.value.map((r) => (
            <li key={r.id} class="pw-row">
              <button class="pw-row-main" onClick={() => (root.value = r)}>
                <Icon name="folder" />
                <span class="pw-row-name">{r.name}</span>
              </button>
            </li>
          ))}
        </ul>
      </section>
    );
  }

  const r = root.value;
  const parts = path.value ? path.value.split('/') : [];
  const here = parts.at(-1) ?? r.name;
  const folders = entries.value;
  return (
    <section class="pw-section" aria-label="Notebooks on this server">
      <h2>Open a notebook on this server</h2>
      {(parts.length > 0 || roots.value.length > 1) && (
        <nav class="pw-crumbs" aria-label="Folder">
          {roots.value.length > 1 && (
            <button
              onClick={() => {
                root.value = null;
                path.value = '';
              }}
            >
              Server
            </button>
          )}
          <button onClick={() => (path.value = '')} aria-current={parts.length === 0 ? 'page' : undefined}>
            {r.name}
          </button>
          {parts.map((p, i) => (
            <button
              key={i}
              onClick={() => (path.value = parts.slice(0, i + 1).join('/'))}
              aria-current={i === parts.length - 1 ? 'page' : undefined}
            >
              {p}
            </button>
          ))}
        </nav>
      )}
      <div class="pw-actions">
        <button class="pw-primary" onClick={() => void launcher.openNas(r, path.value)}>
          <Icon name="book-open" />
          Open “{here}”
        </button>
      </div>
      {folders && folders.length > 0 && (
        <>
          <p class="pw-note">Or open a folder inside it:</p>
          <ul class="pw-list pw-folders">
            {folders.map((e) => (
              <li key={e.path} class="pw-row">
                <button class="pw-row-main" onClick={() => void launcher.openNas(r, e.path)}>
                  <Icon name="book-open" />
                  <span class="pw-row-name">{e.name}</span>
                </button>
                <button
                  class="pn-icon-button pw-row-into"
                  title={`Folders in ${e.name}`}
                  aria-label={`Show the folders in ${e.name}`}
                  onClick={() => (path.value = e.path)}
                >
                  <Icon name="chevron-right" />
                </button>
              </li>
            ))}
          </ul>
        </>
      )}
    </section>
  );
}
