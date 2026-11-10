import { render } from 'preact';
import { EditorState, MarkdownEditor, type ViewMode } from '../src/index.ts';
import '../src/styles.css';

const sample = `# Puffinote

A paragraph with **bold**, *italic*, ~~struck~~, ==highlighted== and \`inline code\`.
A [link](https://example.com) and an autolink https://example.com/path.

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
`;

const state = new EditorState(sample);
(window as unknown as { editorState: EditorState }).editorState = state;

function App() {
  const mode = state.mode.value;
  const set = (m: ViewMode) => () => (state.mode.value = m);
  return (
    <>
      <div class="bar">
        {(['live', 'source', 'raw'] as const).map((m) => (
          <button key={m} aria-pressed={mode === m} onClick={set(m)}>
            {m}
          </button>
        ))}
      </div>
      <div class="host">
        <MarkdownEditor state={state} autoFocus placeholder="Start writing…" />
      </div>
    </>
  );
}

render(<App />, document.getElementById('app')!);
