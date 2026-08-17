# Pivotask

A Trello-inspired task board. One board lives in one JSON file.

Part of the [Puffinote](../../README.md) family, alongside [Typage](../EditMarkdown/) (Markdown),
[Cellestra](../EditSpreadsheets/) (spreadsheets), and [Plaintain](../EditPlaintext/) (plaintext). It
builds, tests, and ships on its own, and Puffinote composes it as a fourth pane type.

## What it does

Columns of cards. Tick them, star them, drag them between columns, open one for its notes and checklist.
Board view or list view, four filters, and a search that reaches into step text.

The file is `.pivotask` — plain JSON inside, on purpose, so it stays readable, diffable, and openable by
anything. A save that changed nothing produces byte-identical output, so it never shows up in a diff, and
a field written by a newer version survives being opened and saved by an older one.

Dates are dates: `YYYY-MM-DD`, no times, no timezones, no recurrence.

## Build and run

```bash
just build-pivotask
just test-pivotask
just run-pivotask               # a GUI — needs a real display, not the devcontainer
just run-pivotask-cli list --due-tomorrow
```

Ship it:

```bash
just install-pivotask           # the `pivotask` CLI onto PATH
just install-pivotask-app       # Pivotask.app into /Applications (macOS)
```

Every install has an exact inverse (`uninstall-pivotask`, `uninstall-pivotask-app`).

## The `pivotask` CLI

```bash
pivotask work.pivotask                          # open the board in the app
pivotask list --due-tomorrow                    # what's about to bite
pivotask add "Renew the domain" --group "To do" --due 2026-09-01
pivotask done "Renew the domain"                # by id, or by part of the title
pivotask move k3f9a2b --group "In progress"
pivotask show k3f9a2b
pivotask groups
```

It finds the single `*.pivotask` in the working directory on its own (boards written before the rename,
`*.pivotask.json`, count too). With none, or with several,
it says what to do instead — guessing which board `pivotask done` meant is the kind of help that loses work.

## Docs

- [docs/PRD.md](docs/PRD.md) — what it does and why
- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) — how it is built
- [docs/pivotask.schema.json](docs/pivotask.schema.json) — the file format
- [AGENTS.md](AGENTS.md) — working in this area
- [proposal 0004](../../docs/proposals/0004-pivotask.md) — the design rationale

MIT licensed.
