import type { ReactNode } from "react";

export function Checkbox({
  checked,
  onChange,
  children,
  className = "",
}: {
  checked: boolean;
  onChange: (v: boolean) => void;
  children: ReactNode;
  className?: string;
}) {
  return (
    <label className={`flex cursor-pointer select-none items-center gap-2 ${className}`}>
      <input type="checkbox" checked={checked} onChange={(e) => onChange(e.target.checked)} className="accent-accent2" />
      {children}
    </label>
  );
}
