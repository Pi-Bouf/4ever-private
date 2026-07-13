import type { ReactNode } from "react";

export function Container({ children }: { children: ReactNode }) {
  return <div className="mx-auto max-w-[1400px] px-5 pb-24 pt-5">{children}</div>;
}
