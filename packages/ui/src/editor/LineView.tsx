import { parseLinePrefix, renderHtmlBody } from '@puffinote/markdown';
import { TextDocument } from '@puffinote/text';
import { memo } from 'preact/compat';
import { lineViewPropsEqual, type LineViewProps } from './lineProps.ts';
import { checkboxLabel, renderLine, type Segment } from './lineRender.ts';
import type { ViewMode } from './reveal.ts';

/** One source line. Memoized by `lineViewPropsEqual`: a line redraws only when what it draws changes. */
export const LineView = memo(function LineView(p: LineViewProps) {
  const r = renderLine(p.text, p.start, p.tokens, p.reveal, p.mode, p.block, p.listDepth);
  const view = (s: Segment) => <SegmentView key={s.from} s={s} text={p.text} resolveImage={p.resolveImage} />;
  const hang = hangingPrefix(p.text, r.segments, p.mode);
  return (
    <div
      class={hang > 0 ? `${r.className} pn-hang` : r.className}
      data-line={p.index}
      data-length={r.length}
      style={r.collapsed ? { display: 'none' } : undefined}
    >
      {hang > 0 ? (
        <>
          {r.segments.slice(0, hang).map(view)}
          {/* The item's text, in a column of its own so wrapped rows line up with it (PRD §10.2). */}
          <span class="pn-li-body">{r.segments.slice(hang).map(view)}</span>
        </>
      ) : (
        r.segments.map(view)
      )}
    </div>
  );
}, lineViewPropsEqual);

/**
 * How many leading segments are a list item's marker (indent, quote, bullet or number, checkbox), which get a
 * column of their own; 0 for a line that isn't a list item, in Raw text, or whose runs don't break where the
 * item's text starts. Shown or hidden, the marker keeps its column, so the text doesn't move under the caret.
 */
function hangingPrefix(text: string, segments: readonly Segment[], mode: ViewMode): number {
  if (mode === 'raw') return 0;
  const p = parseLinePrefix(text);
  if (!p.hasList) return 0;
  let n = 0;
  while (n < segments.length && segments[n]!.to <= p.length) n++;
  const clean = n > 0 && (n === segments.length || segments[n]!.from >= p.length);
  return clean ? n : 0;
}

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
            aria-label={checkboxLabel(text, d.task ?? 'notStarted')}
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
