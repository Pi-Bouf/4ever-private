import { Select, type SelectOption } from "../ui/Select";

export function TypeFilter({
  value,
  onChange,
  types,
}: {
  value: string;
  onChange: (v: string) => void;
  types: Record<string, string>;
}) {
  const options: SelectOption[] = [
    { value: "all", label: "All types" },
    ...Object.entries(types).map(([v, label]) => ({ value: v, label })),
  ];
  return (
    <Select
      value={value}
      onChange={onChange}
      options={options}
      className="rounded-[10px] border border-border bg-panel px-3 py-[11px] text-sm text-text outline-none focus:border-accent"
    />
  );
}
