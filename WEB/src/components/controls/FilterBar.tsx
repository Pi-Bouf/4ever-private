import type { ReactNode } from "react";

export function FilterBar({ children }: { children: ReactNode }) {
  return (
    <div className="sticky top-0 z-10 mb-3.5 flex flex-wrap items-center gap-2.5 bg-bg/95 py-2 backdrop-blur">
      {children}
    </div>
  );
}
