import { IconImage } from "../items/IconImage";

export function IconCell({ id }: { id: number }) {
  return (
    <div className="rounded-lg border border-border bg-panel px-1 py-[7px] text-center">
      <IconImage id={id} size={44} className="mx-auto" />
      <span className="mt-1 block text-[10.5px] text-muted">#{id}</span>
    </div>
  );
}
