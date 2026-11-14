# Puffinote

A notes app for a folder of Markdown files. You open a folder, and Puffinote shows it as a tree, opens notes in tabs, and formats them as you type. Your notes stay plain `.md` files that any other editor, sync tool or git can use, and the same app runs on the desktop, in the browser and on your NAS.

![Puffinote on macOS in the dark theme, with a notebook named Notes open. The sidebar on the left shows the file tree: the folders attachments, Birds (with the note Puffins selected), Journal, Projects and Reading, and the notes Inbox and Welcome. Two tabs are open, Welcome and Puffins. The editor shows the Puffins note formatted in place: a large heading; a paragraph with a bold word, an italic genus name, a highlighted phrase, a blue link to the Wikipedia article and inline code; a photo of an Atlantic puffin standing on a grassy cliff edge above the sea; a quote with a bar about puffin chicks; a "Trip to Látrabjarg" checklist with one task done (ticked and struck through), one in progress and two open; and a table of the three puffin species with their scientific names in italics, lengths and conservation status. The status bar reads "Ln 6, Col 1", "248 words", and the view switcher Rich, Formatted and Raw.](docs/images/notebook.webp)

- **Formats as you type.** Headings, emphasis, lists, quotes, tables and pictures show formatted, and the Markdown markers stay hidden until the caret reaches them.
- **Your files stay yours.** There is no database: a note is a `.md` file, and changes made by other apps show up without overwriting your edits.
- **Works from the keyboard.** `⌘K` runs any command, `⌘O` opens any file by name, and the sidebar searches every note.

## Getting started

Download the desktop app for macOS, Windows or Linux from the [Releases page](https://github.com/gridlocdev/puffinote/releases/latest), or run it on your NAS with Docker:

```bash
docker build -t puffinote . && docker run -d -p 8790:8790 -v /volume1/notes:/data puffinote
```

See [Getting started](docs/getting-started.md) for which file to pick, the first-launch warnings, and the NAS settings.

## Learn more

- [Features](docs/features.md) · [Controls](docs/controls.md) · [Credits](docs/credits.md)
- [All documentation](docs/README.md) · [Working on the code](docs/development.md)

## License

[MIT](LICENSE)
