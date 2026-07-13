import type { ReactNode } from "react";

export function Strong({ children, className = "" }: { children: ReactNode; className?: string }) {
  return <strong className={`font-bold ${className}`}>{children}</strong>;
}
