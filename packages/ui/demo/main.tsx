import { render } from 'preact';
import { MemoryStore, Notebook, Shell, ShellModel, memoryPrefs } from '../src/index.ts';
import '../src/styles.css';

const welcome = `# Welcome to Puffinote

A paragraph with **bold**, *italic*, ~~struck~~, ==highlighted== and \`inline code\`.
A [link](https://example.com) and a [note link](Projects/Roadmap.md).

## Lists

- First item
- Second item
    - Nested item
1. One
2. Two

- [ ] Not started
- [/] In progress
- [x] Done

> A quote with **emphasis**.

---

| Name | Value |
| ---- | ----- |
| a    | 1     |
| b    | 2     |

\`\`\`ts
const x = 1;
\`\`\`

![A picture](Attachments/dot.svg)
`;

const dot = `<svg xmlns="http://www.w3.org/2000/svg" width="120" height="60"><rect width="120" height="60" rx="8" fill="#2f6fdf"/><circle cx="30" cy="30" r="14" fill="#fff"/></svg>`;

// The playground: the whole shell over an in-memory notebook. `pnpm --filter @puffinote/ui demo`.
const store = new MemoryStore('Sample Notebook', {
  'Welcome.md': welcome,
  'Ideas.md': '# Ideas\n\n- A garden journal\n- Plan the trip\n',
  'Projects/Roadmap.md': '# Roadmap\n\n## Phase 1\n\nShip it.\n\n## Phase 2\n\nPlan the next one.\n',
  'Projects/Meeting notes.md': '# Meeting notes\n\nDiscussed the plan for Q3.\n',
  'Projects/Archive/2025.md': '# 2025\n\nOld plans.\n',
  'Attachments/dot.svg': dot,
  'Attachments/report.pdf': '%PDF-1.4 not really',
  'Journal/2026-11-08.md': '# Sunday\n\nQuiet day.\n',
  'Journal/2026-11-09.md': '# Monday\n\nWrote the shell.\n',
});
const prefs = memoryPrefs({ autoSave: true });
const model = new ShellModel({ openNotebook: () => alert('Hosts provide a folder picker (phase 5).') });
Object.assign(window, { store, model });

void Notebook.open(store, {
  prefs,
  download: (name, bytes, mime) => {
    const url = URL.createObjectURL(new Blob([bytes as Uint8Array<ArrayBuffer>], { type: mime }));
    const a = Object.assign(document.createElement('a'), { href: url, download: name });
    a.click();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  },
}).then(async (nb) => {
  model.setNotebook(nb);
  if (nb.tabs.value.length === 0) await model.openFile('Welcome.md');
});

render(<Shell model={model} prefs={prefs} />, document.getElementById('app')!);
