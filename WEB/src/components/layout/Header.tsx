import type { Meta } from "../../types";

export function Header({ meta }: { meta: Meta | null }) {
  return (
    <header className="mb-1.5 flex flex-wrap items-baseline gap-x-4 gap-y-1">
      <h1 className="text-[22px] font-semibold tracking-wide">4Story Item Browser</h1>
      {meta && (
        <span className="text-[13px] text-muted">
          {meta.totalItems.toLocaleString()} items · {meta.galleryIcons.toLocaleString()} icons · TypeScript + Prisma + Vite 8
        </span>
      )}
    </header>
  );
}
