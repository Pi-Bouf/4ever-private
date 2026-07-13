import { typeColor } from "../../lib/itemTypes";

export function PlaceholderIcon({ id, type, size = 40 }: { id: number; type: number; size?: number }) {
  return (
    <div
      className="grid place-items-center rounded-[7px] p-0.5 text-center font-bold leading-none text-white"
      style={{ background: typeColor(type), width: size, height: size, fontSize: size > 48 ? 13 : 10 }}
    >
      {id}
    </div>
  );
}
