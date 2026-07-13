import { Button } from "../ui/Button";

export function DiffStatCard({
  label,
  value,
  hint,
  active,
  onClick,
}: {
  label: string;
  value: number;
  hint?: string;
  active: boolean;
  onClick: () => void;
}) {
  return (
    <Button
      onClick={onClick}
      className={`flex flex-col items-start rounded-[10px] border px-3.5 py-3 text-left transition-colors ${
        active ? "border-accent2 bg-panel2" : "border-border bg-panel hover:border-borderlt"
      }`}
    >
      <span className="text-[22px] font-bold leading-none">{value.toLocaleString()}</span>
      <span className="mt-1 text-[12.5px]">{label}</span>
      {hint && <span className="mt-0.5 text-[11px] text-muted">{hint}</span>}
    </Button>
  );
}
