import { FlagChip } from "./FlagChip";

export function FlagList({ flags }: { flags: string[] }) {
  return (
    <div className="mt-2 flex flex-wrap gap-1.5">
      {flags.map((f) => (
        <FlagChip key={f} flag={f} />
      ))}
    </div>
  );
}
