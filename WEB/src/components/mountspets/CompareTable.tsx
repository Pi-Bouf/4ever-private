import { CompareRow } from "./CompareRow";

export function CompareTable({
  colA,
  colB,
  rows,
}: {
  colA: string;
  colB: string;
  rows: [string, string, string][];
}) {
  return (
    <div className="max-w-2xl overflow-hidden rounded-[12px] border border-border">
      <div className="grid grid-cols-[1.4fr_1fr_1fr] bg-panel2 text-[12px] font-semibold">
        <div className="px-3.5 py-2.5 text-muted">Metric</div>
        <div className="px-3.5 py-2.5 text-[#7fc0ff]">{colA}</div>
        <div className="px-3.5 py-2.5 text-[#f0c078]">{colB}</div>
      </div>
      {rows.map((r, i) => (
        <CompareRow key={r[0]} row={r} alt={i % 2 === 1} />
      ))}
    </div>
  );
}
