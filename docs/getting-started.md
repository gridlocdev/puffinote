# Getting started

You can run Puffinote as a desktop app, on a NAS with Docker, or from source in your browser.

## What you need

- For the desktop app: macOS (Apple silicon or Intel), Windows or Linux.
- For the NAS: Docker.
- For running from source: Node 22.18 or later, pnpm (through `corepack enable`) and [just](https://github.com/casey/just).

## Download

Get the desktop app from the [Releases page](https://github.com/gridlocdev/puffinote/releases/latest):

| System                | File                        |
| --------------------- | --------------------------- |
| macOS (Apple silicon) | `Puffinote-…-mac-arm64.dmg` |
| macOS (Intel)         | `Puffinote-…-mac-x64.dmg`   |
| Windows               | `Puffinote-…-win-x64.exe`   |
| Linux                 | `Puffinote-….AppImage`      |

The apps aren't signed by Apple or Microsoft yet, so the first launch needs one extra step:

- **macOS** says Puffinote "can't be opened" or "is damaged". Open **System Settings > Privacy & Security**, scroll down, and click **Open Anyway** next to Puffinote. Or run this once after moving the app to Applications:

  ```bash
  xattr -dr com.apple.quarantine /Applications/Puffinote.app
  ```

- **Windows** shows "Windows protected your PC". Click **More info**, then **Run anyway**.
- **Linux**: make the AppImage executable (`chmod +x Puffinote-*.AppImage`), then run it.

### Updates

The desktop app checks for a new version once a day and tells you when one is out; **Download** opens the release page. It never installs anything by itself. You can turn the check off in the dialog, or run it from **Check for Updates…** in the app menu (Help on Windows and Linux).

## Run it on a NAS (Docker)

```bash
docker build -t puffinote .
docker run -d -p 8790:8790 -e PUFFINOTE_TOKEN=change-me -v /volume1/notes:/data puffinote
```

Then open `http://<nas>:8790` and enter the token. To expose several folders or run as your NAS user, see [compose.example.yaml](../compose.example.yaml).

| Variable | Default | Meaning |
| --- | --- | --- |
| `PUFFINOTE_ROOTS` | `Notes=/data` | The folders to show, as `Name=/path;Other=/path2` |
| `PUFFINOTE_TOKEN` | unset | The browser asks for this token before it shows any files. Required unless the server listens on loopback only |
| `PUFFINOTE_ALLOW_NO_TOKEN` | unset | `1` lets the server listen on the network without a token, for a reverse proxy that already asks for a password |
| `PUFFINOTE_HOST` | `0.0.0.0` | The address to listen on (`127.0.0.1` answers this machine only) |
| `PUFFINOTE_HOSTS` | unset | Without a token, the extra host names to answer (comma-separated) |
| `PORT` | `8790` | The port to listen on |
| `PUFFINOTE_MAX_MB` | `64` | The largest file you can save, in MB |

Anyone who can reach the server can change and delete your notes, so it refuses to start on a network address (such as the default `0.0.0.0`) without `PUFFINOTE_TOKEN`. If your NAS's reverse proxy already asks for a password, set `PUFFINOTE_ALLOW_NO_TOKEN=1` instead, and publish the port only to the proxy.

Without `PUFFINOTE_TOKEN`, the server only answers requests addressed to an IP address or `localhost`. If you open it by name (for example `http://nas.lan:8790`), list that name in `PUFFINOTE_HOSTS=nas.lan`. Other names get `421 Misdirected Request`, which stops a hostile web page from reaching your notes through DNS rebinding.

If someone else saved a note after you opened it, Puffinote warns you before overwriting their changes.

## Run it from source

```bash
just setup     # corepack enable, then pnpm install
just run-web   # the web client on http://localhost:5173, plus the NAS server on :8790
```

In the browser, Puffinote opens a folder from your disk through the File System Access API (Chromium browsers), or keeps notes in the browser's private storage. See [Development](development.md) for every other way to run and build it.

## First run

The welcome screen lists your recent notebooks and the ways to open one in this host: a folder on disk in the desktop app, a folder from the server on a NAS, or a folder in Chrome and Edge. The last notebook reopens on start when it can do so without asking. Firefox and Safari can't open folders, so they skip the welcome screen and open a notebook kept in the browser's own storage.

Each notebook keeps its own settings in a `.puffinote` folder inside it, so they travel with the folder. The desktop app's update settings live in `updates.json` in the app's user data folder.
