const CLASSES: Record<string, string> = {
  cash: "bg-[#3a2a12] text-[#f0c078]",
  rare: "bg-[#2c1836] text-[#d99cf0]",
};

export function FlagChip({ flag }: { flag: string }) {
  return (
    <span className={`rounded-full px-2 py-0.5 text-[10.5px] capitalize ${CLASSES[flag] ?? "bg-chip text-muted"}`}>
      {flag}
    </span>
  );
}
