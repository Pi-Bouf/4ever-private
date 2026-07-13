export function ResultCount({ count, noun = "results" }: { count: number; noun?: string }) {
  return (
    <span className="whitespace-nowrap text-[13px] text-muted">
      {count.toLocaleString()} {noun}
    </span>
  );
}
