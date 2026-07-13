import type { ReactNode } from "react";

export function TabBar({ children }: { children: ReactNode }) {
  return <div className="my-3.5 flex gap-1.5">{children}</div>;
}
