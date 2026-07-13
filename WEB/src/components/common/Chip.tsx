import type { ReactNode } from "react";

export function Chip({ children, className = "" }: { children: ReactNode; className?: string }) {
  return (
    <span className={`rounded-full px-2 py-0.5 text-[10.5px] capitalize ${className || "bg-chip text-muted"}`}>
      {children}
    </span>
  );
}
