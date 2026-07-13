import type { DiffEntry } from "../../types";
import { DiffRow } from "./DiffRow";

export function DiffList({ entries, types }: { entries: DiffEntry[]; types: Record<string, string> }) {
  return (
    <div className="flex flex-col gap-2">
      {entries.map((e) => (
        <DiffRow key={e.id} entry={e} types={types} />
      ))}
    </div>
  );
}
