export interface Segment<T extends string> {
  value: T;
  label: string;
}

export function SegmentedControl<T extends string>({
  value,
  onChange,
  options,
}: {
  value: T;
  onChange: (v: T) => void;
  options: Segment<T>[];
}) {
  return (
    <div className="mb-4 inline-flex flex-wrap rounded-lg border border-border bg-panel p-1">
      {options.map((o) => (
        <button
          key={o.value}
          onClick={() => onChange(o.value)}
          className={`rounded-md px-3.5 py-1.5 text-sm transition-colors ${
            value === o.value ? "bg-accent2 text-white" : "text-muted hover:text-text"
          }`}
        >
          {o.label}
        </button>
      ))}
    </div>
  );
}
