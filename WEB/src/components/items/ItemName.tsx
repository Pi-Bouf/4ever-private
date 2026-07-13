export function ItemName({ name }: { name: string }) {
  return (
    <div
      className={`truncate text-[13.5px] font-semibold ${name ? "" : "font-normal italic text-muted"}`}
      title={name || "(no name)"}
    >
      {name || "(no name)"}
    </div>
  );
}
