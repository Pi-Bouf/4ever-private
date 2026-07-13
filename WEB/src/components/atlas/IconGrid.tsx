import { IconCell } from "./IconCell";

export function IconGrid({ ids }: { ids: number[] }) {
  return (
    <div className="grid grid-cols-[repeat(auto-fill,minmax(74px,1fr))] gap-2">
      {ids.map((id) => (
        <IconCell key={id} id={id} />
      ))}
    </div>
  );
}
