export function StatRow({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex justify-between gap-2.5 bg-panel2 px-3 py-2">
      <span className="text-[12.5px] text-muted">{label}</span>
      <span className="text-[13px] font-semibold">{value}</span>
    </div>
  );
}
