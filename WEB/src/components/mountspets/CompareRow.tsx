export function CompareRow({ row, alt }: { row: [string, string, string]; alt: boolean }) {
  const [label, a, b] = row;
  return (
    <div className={`grid grid-cols-[1.4fr_1fr_1fr] text-[13px] ${alt ? "bg-panel" : "bg-panel2"}`}>
      <div className="px-3.5 py-2.5 text-muted">{label}</div>
      <div className="px-3.5 py-2.5 font-medium">{a}</div>
      <div className="px-3.5 py-2.5 font-medium">{b}</div>
    </div>
  );
}
