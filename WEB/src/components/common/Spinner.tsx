export function Spinner({ label }: { label?: string }) {
  return (
    <div className="grid place-items-center py-24 text-muted">
      <div className="h-8 w-8 animate-spin rounded-full border-2 border-border border-t-accent" />
      {label && <div className="mt-3 text-sm">{label}</div>}
    </div>
  );
}
