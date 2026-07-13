import type { ReactNode } from "react";

export function Badge({ children }: { children: ReactNode }) {
  return (
    <span className="whitespace-nowrap rounded-full bg-chip px-2 py-0.5 text-[10.5px] text-muted">
      {children}
    </span>
  );
}
