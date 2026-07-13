import type { ReactNode } from "react";

export function Banner({ children }: { children: ReactNode }) {
  return (
    <div className="mb-3.5 rounded-lg border border-[#274a30] bg-[#16281a] px-3.5 py-2.5 text-[12.5px] leading-relaxed text-[#b7e6c1]">
      {children}
    </div>
  );
}
