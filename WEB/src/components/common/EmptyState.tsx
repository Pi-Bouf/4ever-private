import type { ReactNode } from "react";

export function EmptyState({ children }: { children: ReactNode }) {
  return <div className="py-16 text-center text-muted">{children}</div>;
}
