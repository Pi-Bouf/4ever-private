import type { ReactNode } from "react";
import { Button } from "../ui/Button";

export function Tab({ active, onClick, children }: { active: boolean; onClick: () => void; children: ReactNode }) {
  return (
    <Button
      onClick={onClick}
      className={`rounded-lg border px-4 py-2 text-sm transition-colors ${
        active ? "border-accent2 bg-accent2 text-white" : "border-border bg-panel text-text hover:border-borderlt"
      }`}
    >
      {children}
    </Button>
  );
}
