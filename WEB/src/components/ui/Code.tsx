import type { ReactNode } from "react";

export function Code({ children }: { children: ReactNode }) {
  return <code className="rounded bg-white/10 px-1 text-[12px]">{children}</code>;
}
