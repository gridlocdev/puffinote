import { useEffect, useLayoutEffect, useRef, useState } from 'preact/hooks';
import { Icon } from './Icon.tsx';
import type { PicturePreview as Preview } from './shellModel.ts';

/** The most the preview zooms in (800%). */
export const MAX_PREVIEW_ZOOM = 8;
const STEP = 1.25;
const MARGIN = 32;

/** Where the picture sits: its scale, and its centre's offset from the view's centre, in screen pixels. */
export interface View {
  readonly scale: number;
  readonly x: number;
  readonly y: number;
}

/** The scale that fits a `w`×`h` picture in a `vw`×`vh` view, never enlarging it. */
export function fitScale(w: number, h: number, vw: number, vh: number): number {
  if (w <= 0 || h <= 0) return 1;
  return Math.min(1, vw / w, vh / h);
}

/**
 * Keep the picture in view: along an axis where it is smaller than the view it is centred, and where it is
 * larger its edge never comes in past the view's edge.
 */
export function clampView(v: View, w: number, h: number, vw: number, vh: number): View {
  const lim = (size: number, view: number) => Math.max(0, (size - view) / 2);
  const lx = lim(w * v.scale, vw);
  const ly = lim(h * v.scale, vh);
  const clamp = (n: number, l: number) => Math.max(-l, Math.min(l, n)) + 0; // + 0 turns -0 into 0
  return { scale: v.scale, x: clamp(v.x, lx), y: clamp(v.y, ly) };
}

/**
 * Zoom to `scale` (clamped between the fit scale and 800%), keeping the picture point under (`px`, `py`),
 * given from the view's centre, where it was.
 */
export function zoomAt(v: View, scale: number, px: number, py: number, min: number): View {
  const s = Math.max(min, Math.min(MAX_PREVIEW_ZOOM, scale));
  const k = s / v.scale;
  return { scale: s, x: px - (px - v.x) * k, y: py - (py - v.y) * k };
}

/**
 * The picture preview (PRD §10.6): the picture alone over a dimmed window, fitted to it. Pinch or ⌘-scroll to
 * zoom, scroll or drag to pan, double-click to switch between fitted and 100%, `+` `-` `0` from the keyboard.
 * Right-click shows the picture's menu; Escape or a click outside the picture closes it.
 */
export function PicturePreview(props: {
  readonly preview: Preview;
  readonly onClose: () => void;
  readonly onMenu: (at: { x: number; y: number }) => void;
}) {
  const { preview } = props;
  const stage = useRef<HTMLDivElement>(null);
  const [natural, setNatural] = useState<{ w: number; h: number } | null>(null);
  const [box, setBox] = useState({ w: 0, h: 0 });
  const [view, setView] = useState<View | null>(null);
  const [failed, setFailed] = useState(false);
  const drag = useRef<{
    x: number;
    y: number;
    vx: number;
    vy: number;
    moved: boolean;
    onPicture: boolean;
  } | null>(null);

  useLayoutEffect(() => {
    stage.current?.focus({ preventScroll: true });
    const el = stage.current;
    if (!el) return;
    const measure = () => setBox({ w: el.clientWidth, h: el.clientHeight });
    measure();
    const ro = new ResizeObserver(measure);
    ro.observe(el);
    return () => ro.disconnect();
  }, []);

  // A new picture starts fitted.
  useEffect(() => {
    setNatural(null);
    setView(null);
    setFailed(false);
  }, [preview.src]);

  // Fitted, the picture keeps a margin from the window's edges.
  const fit = natural ? fitScale(natural.w, natural.h, box.w - 2 * MARGIN, box.h - 2 * MARGIN) : 1;
  const current = view ?? { scale: fit, x: 0, y: 0 };
  const shown = natural ? clampView(current, natural.w, natural.h, box.w, box.h) : current;

  // Events can come faster than renders (a pinch, a fling): each one builds on the last one's view.
  const latest = useRef(shown);
  latest.current = shown;
  const set = (v: View) => {
    if (!natural) return;
    latest.current = clampView(v, natural.w, natural.h, box.w, box.h);
    setView(latest.current);
  };
  /** Zoom by `factor` around a point given in client coordinates (the view's centre when omitted). */
  const zoomBy = (factor: number, cx?: number, cy?: number) => {
    const r = stage.current?.getBoundingClientRect();
    const px = r && cx !== undefined ? cx - r.left - r.width / 2 : 0;
    const py = r && cy !== undefined ? cy - r.top - r.height / 2 : 0;
    set(zoomAt(latest.current, latest.current.scale * factor, px, py, Math.min(fit, 1)));
  };
  const toggleActual = (cx?: number, cy?: number) => {
    const r = stage.current?.getBoundingClientRect();
    if (Math.abs(latest.current.scale - fit) > 0.001) return setView(null);
    const px = r && cx !== undefined ? cx - r.left - r.width / 2 : 0;
    const py = r && cy !== undefined ? cy - r.top - r.height / 2 : 0;
    set(zoomAt(latest.current, fit < 1 ? 1 : Math.min(MAX_PREVIEW_ZOOM, fit * 2), px, py, fit));
  };

  const onWheel = (e: WheelEvent) => {
    e.preventDefault();
    if (e.ctrlKey || e.metaKey) {
      // A trackpad pinch arrives as a wheel with Control held; ⌘-scroll zooms too.
      zoomBy(Math.exp(-e.deltaY * (e.deltaMode === 1 ? 0.05 : 0.0025)), e.clientX, e.clientY);
    } else {
      set({ ...latest.current, x: latest.current.x - e.deltaX, y: latest.current.y - e.deltaY });
    }
  };

  // Wheel listeners from Preact are passive in some engines; this one must cancel the page's scroll and zoom.
  const wheelRef = useRef(onWheel);
  wheelRef.current = onWheel;
  useEffect(() => {
    const el = stage.current;
    if (!el) return;
    const h = (e: WheelEvent) => wheelRef.current(e);
    el.addEventListener('wheel', h, { passive: false });
    return () => el.removeEventListener('wheel', h);
  }, []);

  const onKeyDown = (e: KeyboardEvent) => {
    e.stopPropagation();
    const mod = e.metaKey || e.ctrlKey;
    if (e.key === 'Escape') props.onClose();
    else if (e.key === '+' || e.key === '=') zoomBy(STEP);
    else if (e.key === '-' || e.key === '_') zoomBy(1 / STEP);
    else if (e.key === '0') setView(null);
    else if (e.key === '1' && !mod) toggleActualSize();
    else if (e.key.startsWith('Arrow')) {
      const d = e.shiftKey ? 200 : 50;
      const dx = e.key === 'ArrowLeft' ? d : e.key === 'ArrowRight' ? -d : 0;
      const dy = e.key === 'ArrowUp' ? d : e.key === 'ArrowDown' ? -d : 0;
      set({ ...latest.current, x: latest.current.x + dx, y: latest.current.y + dy });
    } else if (e.key === 'ContextMenu' || (e.key === 'F10' && e.shiftKey)) {
      const r = stage.current!.getBoundingClientRect();
      props.onMenu({ x: r.left + r.width / 2, y: r.top + r.height / 2 });
    } else return;
    e.preventDefault();
  };
  const toggleActualSize = () => set({ scale: 1, x: latest.current.x, y: latest.current.y });

  const onPointerDown = (e: PointerEvent) => {
    if (e.button !== 0) return;
    // Captured, the pointer's later events all target the stage: note now whether it went down on the picture.
    const onPicture = !!(e.target as HTMLElement).closest('.pn-preview-img');
    (e.currentTarget as HTMLElement).setPointerCapture(e.pointerId);
    drag.current = {
      x: e.clientX,
      y: e.clientY,
      vx: latest.current.x,
      vy: latest.current.y,
      moved: false,
      onPicture,
    };
  };
  const onPointerMove = (e: PointerEvent) => {
    const d = drag.current;
    if (!d) return;
    const dx = e.clientX - d.x;
    const dy = e.clientY - d.y;
    if (!d.moved && Math.hypot(dx, dy) < 3) return;
    d.moved = true;
    set({ ...latest.current, x: d.vx + dx, y: d.vy + dy });
  };
  const onPointerUp = () => {
    const d = drag.current;
    drag.current = null;
    // A click (not a drag) outside the picture closes the preview.
    if (d && !d.moved && !d.onPicture) props.onClose();
  };

  const percent = Math.round(shown.scale * 100);
  const draggable = natural !== null && (natural.w * shown.scale > box.w || natural.h * shown.scale > box.h);
  return (
    <div class="pn-preview" role="dialog" aria-modal="true" aria-label={preview.name}>
      <div class="pn-preview-bar">
        <span class="pn-preview-name" title={preview.name}>
          {preview.name}
        </span>
        <div class="pn-preview-tools">
          <button type="button" title="Zoom Out (−)" aria-label="Zoom Out" onClick={() => zoomBy(1 / STEP)}>
            <Icon name="zoom-out" />
          </button>
          <button
            type="button"
            class="pn-preview-percent"
            title={Math.abs(shown.scale - fit) > 0.001 ? 'Fit to Window (0)' : 'Actual Size (1)'}
            onClick={() => toggleActual()}
          >
            {percent}%
          </button>
          <button type="button" title="Zoom In (+)" aria-label="Zoom In" onClick={() => zoomBy(STEP)}>
            <Icon name="zoom-in" />
          </button>
          <button type="button" title="Close (Esc)" aria-label="Close" onClick={props.onClose}>
            <Icon name="x" />
          </button>
        </div>
      </div>
      <div
        ref={stage}
        class={`pn-preview-stage${draggable ? ' pn-preview-pan' : ''}`}
        tabIndex={-1}
        onKeyDown={onKeyDown}
        onPointerDown={onPointerDown}
        onPointerMove={onPointerMove}
        onPointerUp={onPointerUp}
        onPointerCancel={() => (drag.current = null)}
        onDblClick={(e) => {
          if ((e.target as HTMLElement).closest('.pn-preview-img')) toggleActual(e.clientX, e.clientY);
        }}
        onContextMenu={(e) => {
          e.preventDefault();
          props.onMenu({ x: e.clientX, y: e.clientY });
        }}
      >
        {failed ? (
          <div class="pn-preview-missing">Puffinote couldn’t load this picture.</div>
        ) : (
          <img
            class="pn-preview-img"
            src={preview.src}
            alt={preview.name}
            draggable={false}
            style={
              natural
                ? {
                    width: `${natural.w}px`,
                    height: `${natural.h}px`,
                    transform: `translate(-50%, -50%) translate(${shown.x}px, ${shown.y}px) scale(${shown.scale})`,
                  }
                : { visibility: 'hidden' }
            }
            onLoad={(e) => {
              const img = e.currentTarget as HTMLImageElement;
              setNatural({ w: img.naturalWidth, h: img.naturalHeight });
            }}
            onError={() => setFailed(true)}
          />
        )}
      </div>
    </div>
  );
}
