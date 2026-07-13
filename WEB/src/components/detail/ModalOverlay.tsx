import type { ReactNode } from "react";

export function ModalOverlay({ onClose, children }: { onClose: () => void; children: ReactNode }) {
  return (
    <div
      className="fixed inset-0 z-50 grid place-items-center bg-black/60 p-5 backdrop-blur-[2px]"
      onClick={onClose}
    >
      <div
        className="relative max-h-[88vh] w-[min(520px,100%)] overflow-auto rounded-[14px] border border-border bg-panel p-5 pb-6 shadow-2xl"
        onClick={(e) => e.stopPropagation()}
      >
        {children}
      </div>
    </div>
  );
}
