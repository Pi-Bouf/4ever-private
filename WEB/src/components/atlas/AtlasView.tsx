import { useIncremental } from "../../hooks/useIncremental";
import { IconGrid } from "./IconGrid";
import { EmptyState } from "../common/EmptyState";

export function AtlasView({ ids, resetKey }: { ids: number[]; resetKey: string }) {
  const { limit, sentinel } = useIncremental(resetKey, 300);
  if (ids.length === 0) return <EmptyState>No icons match.</EmptyState>;
  return (
    <>
      <IconGrid ids={ids.slice(0, limit)} />
      <div ref={sentinel} className="h-px" />
    </>
  );
}
