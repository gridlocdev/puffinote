# Plaintain

A Notepad-class plaintext editor — the tool you reach for when a file is "just text".

Part of the [Puffinote](../../README.md) family, alongside [Typage](../EditMarkdown/) (Markdown) and
[Cellestra](../EditSpreadsheets/) (spreadsheets). It builds, tests, and ships on its own, and Puffinote
composes it as a third pane type.

## What it does

Opens any decodable file and shows it exactly as it is: monospace, uniform rows, a line-number gutter,
word wrap off by default (the Notepad expectation for a log). Encoding and line endings round-trip
byte for byte, and a file whose bytes aren't valid UTF-8 opens **read-only** rather than being rewritten
through a guess.

It does not highlight syntax, fold JSON, or render a CSV as a table. A delimited file is Cellestra's
question; structured config is still text. That line is what keeps this a text editor.

## Build and run

```bash
just build-plaintain
just test-plaintain
just run-plaintain            # a GUI — needs a real display, not the devcontainer
just run-plaintain-cli info server.log
```

Ship it:

```bash
just install-plaintain        # the `plaintain` CLI onto PATH
just install-plaintain-app    # Plaintain.app into /Applications (macOS)
```

Every install has an exact inverse (`uninstall-plaintain`, `uninstall-plaintain-app`).

## The `plaintain` CLI

```bash
plaintain server.log          # open it in Plaintain
plaintain                     # open Plaintain, empty
plaintain info server.log     # encoding, line endings, and line/word/character/byte counts
```

`plaintain info` exits non-zero when the encoding had to be guessed, so a script can branch on "this isn't
text I can vouch for" without parsing the output.

## Docs

- [docs/PRD.md](docs/PRD.md) — what it does and why
- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) — how it is built
- [AGENTS.md](AGENTS.md) — working in this area
- [proposal 0003](../../docs/proposals/0003-plaintain.md) — the design rationale

MIT licensed.
