const svgs = import.meta.glob<string>('../icons/*.svg', { query: '?raw', import: 'default', eager: true });
const byName = new Map(
  Object.entries(svgs).map(([path, svg]) => [path.replace(/^.*\/(.+)\.svg$/, '$1'), svg]),
);

/** A Lucide icon by name (`folder`, `search`, …). Inline SVG so it takes `currentColor`. */
export function Icon({ name, title }: { readonly name: string; readonly title?: string }) {
  const svg = byName.get(name);
  if (!svg) return <span class="pn-icon" />;
  return (
    <span
      class="pn-icon"
      aria-hidden={title ? undefined : true}
      title={title}
      dangerouslySetInnerHTML={{ __html: svg }}
    />
  );
}
