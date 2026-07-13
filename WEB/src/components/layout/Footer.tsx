import type { Meta } from "../../types";

export function Footer({ meta }: { meta: Meta | null }) {
  if (!meta) return null;
  return (
    <footer className="mt-10 border-t border-border pt-4 text-[11.5px] leading-relaxed text-muted">
      <div>
        <span className="text-borderlt">Items:</span> {meta.itemSource}
      </div>
      <div>
        <span className="text-borderlt">Icons:</span> {meta.iconSource}
      </div>
      <div className="mt-1 opacity-70">Generated {new Date(meta.generatedAt).toLocaleString()}</div>
    </footer>
  );
}
