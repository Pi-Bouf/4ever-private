import type { DiffEntry } from "../../types";
import { isBenignEntry } from "../../lib/diffBenign";
import { TypeBadge } from "../items/TypeBadge";
import { ItemId } from "../common/ItemId";
import { DiffBadge } from "./DiffBadge";
import { FieldChange } from "./FieldChange";

export function DiffRow({ entry, types }: { entry: DiffEntry; types: Record<string, string> }) {
  const benign = isBenignEntry(entry);
  return (
    <div
      className={`flex flex-col gap-2 rounded-[10px] border border-border bg-panel px-3 py-2.5 sm:flex-row sm:items-center ${
        benign ? "opacity-70" : ""
      }`}
      title={benign ? "Not a real difference — representation only (see the field tag)." : undefined}
    >
      <div className="flex min-w-0 items-center gap-2 sm:w-60 sm:shrink-0">
        <span className="truncate text-[13.5px] font-semibold" title={entry.name || "(no name)"}>
          {entry.name || "(no name)"}
        </span>
        <ItemId id={entry.id} />
      </div>
      <div className="flex flex-wrap items-center gap-1.5">
        <TypeBadge type={entry.type} types={types} />
        {entry.kind === "changed" ? (
          entry.fields?.map((fd) => <FieldChange key={fd.field} diff={fd} />)
        ) : (
          <DiffBadge kind={entry.kind} />
        )}
      </div>
    </div>
  );
}
