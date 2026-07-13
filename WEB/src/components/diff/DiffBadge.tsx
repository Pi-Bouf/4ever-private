const STYLES: Record<string, string> = {
  onlyDb: "bg-[#12283a] text-[#7fc0ff]",
  onlyFile: "bg-[#3a2a12] text-[#f0c078]",
};
const LABELS: Record<string, string> = {
  onlyDb: "DB only",
  onlyFile: "Game only",
};

export function DiffBadge({ kind }: { kind: "onlyDb" | "onlyFile" }) {
  return <span className={`rounded-full px-2 py-0.5 text-[10.5px] font-medium ${STYLES[kind]}`}>{LABELS[kind]}</span>;
}
