import type { ReactNode } from "react";

export function SectionTitle({ children, hint }: { children: ReactNode; hint?: string }) {
  return (
    <div className="mb-3 mt-7 first:mt-0">
      <h3 className="text-[13px] font-semibold tracking-wide text-text">{children}</h3>
      {hint && <p className="mt-0.5 text-[12px] text-muted">{hint}</p>}
    </div>
  );
}
