import { renderHtmlBody, type Token } from '@puffinote/markdown';
import { TextDocument } from '@puffinote/text';
import { memo } from 'preact/compat';
import type { BlockKind } from './blocks.ts';
import { renderLine, type Segment } from './lineRender.ts';
import type { LineReveal, ViewMode } from './reveal.ts';

export interface LineViewProps {
  readonly index: number;
  readonly text: string;
  readonly start: number;
  readonly tokens: readonly Token[];
  readonly reveal: LineReveal;
  readonly mode: ViewMode;
  readonly block: BlockKind | null;
  readonly resolveImage: (url: string) => string;
}

/** One source line. Memoized: a line redraws only when its text, tokens or reveal state change. */
export const LineView = memo(function LineView(p: LineViewProps) {
  const r = renderLine(p.text, p.start, p.tokens, p.reveal, p.mode, p.block);
  return (
    <div
      class={r.className}
      data-line={p.index}
      data-length={r.length}
      style={r.collapsed ? { display: 'none' } : undefined}
    >
      {r.segments.map((s) => (
        <SegmentView key={s.from} s={s} text={p.text} resolveImage={p.resolveImage} />
      ))}
    </div>
  );
});

function SegmentView({
  s,
  text,
  resolveImage,
}: {
  s: Segment;
  text: string;
  resolveImage: (url: string) => string;
}) {
  if (s.concealed) {
    const d = s.deco;
    if (!d) return null;
    const common = { 'data-from': s.from, 'data-to': s.to, 'data-deco': d.kind };
    switch (d.kind) {
      case 'checkbox':
        return (
          <span
            {...common}
            class={`pn-deco pn-checkbox pn-checkbox-${d.task}`}
            role="checkbox"
            aria-checked={d.task === 'done' ? 'true' : d.task === 'inProgress' ? 'mixed' : 'false'}
          />
        );
      case 'indent':
        return (
          <span {...common} class="pn-deco pn-indent" style={{ width: `${(d.columns ?? 0) * 0.4}em` }} />
        );
      case 'image':
        return (
          <span {...common} class="pn-deco pn-image">
            <img src={resolveImage(d.url ?? '')} alt="" draggable={false} />
          </span>
        );
      case 'rule':
        return <span {...common} class="pn-deco pn-rule" />;
      default:
        return (
          <span {...common} class={`pn-deco pn-deco-${d.kind}`}>
            {d.text}
          </span>
        );
    }
  }
  return (
    <span
      data-from={s.from}
      data-to={s.to}
      class={s.className || undefined}
      title={s.href ?? undefined}
      data-href={s.href ?? undefined}
    >
      {text.slice(s.from, s.to)}
    </span>
  );
}

/** A block drawn as a whole while the caret is outside it: a table. Clicking it reveals its source. */
export const BlockWidget = memo(function BlockWidget({ line, source }: { line: number; source: string }) {
  const html = renderHtmlBody(TextDocument.fromString(source));
  return (
    <div
      class="pn-line pn-widget"
      data-line={line}
      data-length={0}
      data-widget=""
      dangerouslySetInnerHTML={{ __html: html }}
    />
  );
});
