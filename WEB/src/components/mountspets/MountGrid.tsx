import type { Mount } from "../../types";
import { MountCard } from "./MountCard";

export function MountGrid({ mounts, onSelect }: { mounts: Mount[]; onSelect: (m: Mount) => void }) {
  return (
    <div className="grid grid-cols-[repeat(auto-fill,minmax(210px,1fr))] gap-2.5">
      {mounts.map((m) => (
        <MountCard key={m.id} mount={m} onClick={() => onSelect(m)} />
      ))}
    </div>
  );
}
