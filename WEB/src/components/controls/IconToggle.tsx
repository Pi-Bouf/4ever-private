import { Checkbox } from "../ui/Checkbox";

export function IconToggle({ checked, onChange }: { checked: boolean; onChange: (v: boolean) => void }) {
  return (
    <Checkbox checked={checked} onChange={onChange} className="text-[13px] text-muted">
      Show icons
    </Checkbox>
  );
}
