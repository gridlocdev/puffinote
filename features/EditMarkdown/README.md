# Typage

A focused, native **Markdown editor** for desktop and CLI, with **live inline preview** — the
document reads as formatted prose and the syntax markers reveal only where the caret is.

> **Name:** *Typage* (from **type** + **page**).
>
> **Status:** early development, pre-v1. The editing **engine** is substantially built and tested;
> the UI is an early surface. See [what's built](#whats-built-and-whats-not).

---

## Why this exists

Typage is the first deliverable of a larger effort to build an Obsidian-class notes app in Avalonia.
Rather than build the whole notes app first, we isolate the hardest, most reusable piece — the
**live-inline editing surface and its engine** — as a standalone product that is independently useful
and testable. It is built on a deliberately tiny, auditable dependency surface: the **.NET BCL +
Avalonia + OS system services**, and nothing else. No Markdown library, no editor library — the
document model, tokenizer, live-preview logic, and HTML renderer are all owned in-house.

Full reasoning is in **[docs/PRD.md](docs/PRD.md)**.

## What it does (v1 goals)

- Open, edit, and save one `.md` file at a time, like a Markdown-native word processor.
- **Live inline rendering:** headings, bold/italic, code, lists, six-state task lists, quotes, links, images,
  tables, and front matter render in place; markers recede when the caret moves away.
- A **minimal, auditable dependency tree** (.NET BCL + Avalonia + OS services only).
- The editing surface ships as a **clean, embeddable Avalonia control** with a stable API, separable
  from the standalone shell — so it drops into the future notes app unchanged.
- Cross-platform (Windows, macOS, Linux) from one codebase.
- **No data loss:** atomic saves, byte-for-byte encoding and line-ending preservation, session undo.
- **Export to HTML** — self-contained, fully in-house.

Not in v1 (on purpose): multi-file notebooks, tabs, backlinks, a plugin system, full CommonMark/GFM
edge-case fidelity, real-time collaboration, and a bundled PDF library. See the
[PRD §2.2](docs/PRD.md) for the full list.

## Tech at a glance

- **C# on .NET 10**
- **Avalonia 12** for the UI; the editing surface is a custom control over
  `Avalonia.Media.TextFormatting` (not a reused `TextBox`)
- A **pure, UI-free engine** (`Typage.Engine`) holding the immutable document model, grapheme-aware
  navigation, undo/redo, the incremental Markdown tokenizer + checkpoint cache, and the HTML renderer
  — shared by the desktop app and the CLI
- A **`typage` console tool** (`Typage.Cli`) — a second consumer of the engine, built on
  `System.CommandLine`, with **no Avalonia and no network**
- **xUnit v3** for all tests, including headless Avalonia UI tests

Exact dependency versions are pinned at the repo root in `Directory.Packages.props`.

## Project map

```
   ┌──────────────────────────────┐   ┌──────────────────────────────┐
   │        Typage.Desktop         │   │          Typage.Cli           │
   │  window · menu · toolbar ·    │   │  html · tokens · stats ·      │
   │  status bar · file dialogs    │   │  outline · cat                │
   └───────────────┬──────────────┘   └───────────────┬──────────────┘
                   │ hosts                             │ calls into
   ┌───────────────▼──────────────┐                   │
   │        Typage.Control         │                   │
   │  MarkdownEditor surface ·     │                   │
   │  MarkdownTheme · IEmbedResolver│                  │
   └───────────────┬──────────────┘                   │
                   │  calls into                       │
                   └──────────────────┬────────────────┘
                                      ▼
            ┌─────────────────────────────────────────┐
            │              Typage.Engine                │
            │  Document model (piece table) · grapheme  │
            │  nav · undo/redo · incremental tokenizer  │
            │  + checkpoints · HTML renderer            │
            │       (no Avalonia, no UI at all)         │
            └─────────────────────────────────────────┘
```

Dependency direction is strictly **shell → control → engine**; no upward references.

## Getting started

```bash
cd features/EditMarkdown
dotnet build
dotnet test                                   # engine + CLI + headless UI tests
dotnet run --project Typage.Desktop           # launch the app
dotnet run --project Typage.Cli -- html notes.md -o notes.html
```

## What's built, and what's not

The **engine is real and well-tested** — the document model, grapheme navigation, undo/redo, the
incremental tokenizer with its checkpoint cache and an *incremental-equals-full-rebuild* test suite,
and the HTML renderer all pass a green suite, and the `typage` CLI exercises them end to end.

The **UI is an early surface**: it renders the token stream with the theme, handles
caret/selection/keyboard/mouse, dims markers near the caret, re-tokenizes **incrementally** from the
first changed line, and is **line-virtualized** (only the visible slice is drawn). The shell adds
Cellestra-style touches — a **Command Palette** (⌘⇧P) over one command catalog, **Find/Replace**
(plain + regex), a recent-files menu, and a dark theme. It still keeps document offsets 1:1 with the
visual model rather than truly collapsing concealed glyphs. The PRD's
full live-preview behavior (glyph-hiding with a bidirectional offset map), IME, accessibility peer,
smart paste, spell check, and the OS print/PDF path are the dedicated later phases
(PRD §11) and are not built yet. See [AGENTS.md](AGENTS.md) for the honest status and the layering to
build them on.

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- The **`typage` CLI** has no Avalonia dependency, so it runs anywhere .NET 10 does (Linux/CI included).
- The desktop app is cross-platform via Avalonia; it builds and runs on Windows, macOS, and Linux.

## License

MIT — see the repository `LICENSE`.
