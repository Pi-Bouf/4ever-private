import { TextInput } from "../ui/TextInput";

export function SearchBox({
  value,
  onChange,
  placeholder,
}: {
  value: string;
  onChange: (v: string) => void;
  placeholder?: string;
}) {
  return (
    <TextInput
      value={value}
      onChange={onChange}
      placeholder={placeholder ?? "Search…"}
      autoFocus
      className="min-w-[220px] flex-1 rounded-[10px] border border-border bg-panel px-3.5 py-[11px] text-[15px] text-text outline-none focus:border-accent"
    />
  );
}
